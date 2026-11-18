// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Conventions;
using Tellma.Core.Data.Ids;

namespace Tellma.Core.Crud
{
    // Spec 0015 §14.2: the executor raises spec 0012's internal data-layer exceptions, and the
    // pipeline translates those a persist raises into the closed set. The rows here are the save's
    // and the action's; the delete-only rows (the delete-by-query cap and count, Fk.InUse) join them
    // with the deletes. Retries exhausted and the ambiguous commit are the operation's own, as are
    // 50412 and 50401, which the guarded runner raises and every operation passes through.
    /// <summary>Translates the data layer's failures of a persist into the exceptions a caller acts on.</summary>
    internal static class PersistFailures
    {
        /// <summary>The problem code of every refusal for a row outside the grant.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>The problem code of a conflict on a row whose stamp moved.</summary>
        private const string ConflictCode = "concurrency-conflict";

        /// <summary>The stamp's form on the wire: seven fractional digits and the offset, so it round-trips.</summary>
        private const string StampFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffffzzz";

        /// <summary>The unique index two batches race on when both open the first assignment of one target.</summary>
        private const string OpenAssignmentIndex = "UX_Assignments_OpenTarget";

        /// <summary>The code of that race, which the user retries.</summary>
        private const string AssignmentsConcurrentCode = "Assignments.Concurrent";

        /// <summary>The argument a persist-time refusal of a blob reference carries.</summary>
        private const string PersistReason = "persist";

        /// <summary>Translates one failure of a persist, counting the concurrency conflicts it reports.</summary>
        /// <param name="failure">What the persist's execution threw.</param>
        /// <param name="context">What the operation knows about the persist.</param>
        /// <param name="metrics">The pipeline's instruments.</param>
        /// <returns>The exception to raise instead; null when the failure passes through unchanged.</returns>
        public static Exception? Translate(Exception failure, PersistFailureContext context, CrudMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(failure);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(metrics);
            return failure switch
            {
                ConcurrencyConflictException conflict => Conflict(conflict, context, metrics),
                RowSecurityException => new ForbiddenException(ForbiddenCode, context.Resource, context.Action, ForbiddenReason.OutsideFilter),
                BatchAssertionFailedException assertion => Assertion(assertion, context),
                TreeCycleException => Cycle(metrics),
                TreeDepthExceededException => AtRoot(ValidationCodes.TreeTooDeep, Arguments()),
                UniqueConstraintViolationException unique => Unique(unique, context),
                ForeignKeyViolationException foreignKey => ForeignKey(foreignKey, context),
                ArithmeticFailureException arithmetic when context.CarriesQueryText => AtRoot(ValidationCodes.QueryArithmeticFailed, Arguments(("reason", arithmetic.Reason))),
                _ => null,
            };
        }

        // Spec 0015 §8: every conflict missing is not found, since a row that never existed or was
        // deleted under the caller is no concurrency conflict; otherwise the conflicts name each
        // modifier in the request's content language, falling back to the primary name.
        /// <summary>Translates the emitter's conflict rows.</summary>
        /// <param name="conflict">The conflict.</param>
        /// <param name="context">What the operation knows.</param>
        /// <param name="metrics">The pipeline's instruments.</param>
        /// <returns>The exception to raise.</returns>
        private static TellmaException Conflict(ConcurrencyConflictException conflict, PersistFailureContext context, CrudMetrics metrics)
        {
            IReadOnlyList<ConflictRow> rows = conflict.Conflicts;
            int missing = rows.Count(static row => row.IsMissing);
            metrics.CountConflicts(CrudMetrics.MissingConflict, missing);
            metrics.CountConflicts(CrudMetrics.StampConflict, rows.Count - missing);
            return missing == rows.Count
                ? new NotFoundException(context.Resource, [.. rows.Select(static row => row.Id.ToString(CultureInfo.InvariantCulture))])
                : new ConcurrencyException(
                ConflictCode,
                [.. rows.Select(row => new ConcurrencyConflict(
                    row.Id,
                    row.ModifiedAt?.ToString(StampFormat, CultureInfo.InvariantCulture),
                    row.ModifiedById,
                    row.IsMissing ? null : NameOf(row, context.LanguageIndex),
                    row.IsMissing))]);
        }

        /// <summary>A modifier's name in a content language, falling back to the primary name when that twin is empty.</summary>
        /// <param name="row">The conflict row.</param>
        /// <param name="language">The 1-based content language.</param>
        /// <returns>The name.</returns>
        private static string? NameOf(ConflictRow row, int language)
        {
            string? localized = language switch
            {
                2 => row.Name2,
                3 => row.Name3,
                _ => row.Name,
            };
            return string.IsNullOrEmpty(localized) ? row.Name : localized;
        }

        /// <summary>Translates a failed assertion of the platform's bands or of the distribution's.</summary>
        /// <param name="assertion">The assertion.</param>
        /// <param name="context">What the operation knows.</param>
        /// <returns>The exception to raise; null for one that passes through.</returns>
        private static Exception? Assertion(BatchAssertionFailedException assertion, PersistFailureContext context)
        {
            return assertion.Number switch
            {
                TellmaSqlErrors.NotFound => new NotFoundException(context.Resource, [.. assertion.MissingIds.Select(static id => id.ToString(CultureInfo.InvariantCulture))]),

                // Any invariant but the tree cycle, which the executor raises as its own exception; a
                // blob reference refused at persist time says so (spec 0017 §4.4).
                TellmaSqlErrors.Invariant => assertion.Code == ValidationCodes.BlobNotAttachable
                    ? AtRoot(assertion.Code, Arguments(("reason", PersistReason)))
                    : AtRoot(assertion.Code, Arguments()),

                // A distribution's guard: its message is the code.
                >= TellmaSqlErrors.DistroMin and <= TellmaSqlErrors.DistroMax => AtRoot(assertion.Code, Arguments()),
                _ => null,
            };
        }

        /// <summary>Translates a cycle the path-count fence found, counting it as a conflict between two saves.</summary>
        /// <param name="metrics">The pipeline's instruments.</param>
        /// <returns>The exception to raise.</returns>
        private static ValidationException Cycle(CrudMetrics metrics)
        {
            // The validator refused every cycle the payload closes alone: one found at persist time
            // was closed together with a save that committed since.
            metrics.CountConflicts(CrudMetrics.CycleConflict, 1);
            return AtRoot(ValidationCodes.TreeCycle, Arguments());
        }

        // Spec 0015 §14.2: a unique index of the stack's tables is the race the validator could not
        // see, reported at the one payload row carrying the property, else at the root naming the
        // property; a blob reference's index is two saves attaching one staged blob; the open
        // assignment's index is two batches opening one target's first assignment.
        /// <summary>Translates a unique index violation.</summary>
        /// <param name="unique">The violation.</param>
        /// <param name="context">What the operation knows.</param>
        /// <returns>The exception to raise; null for an index the stack's tables do not hold.</returns>
        private static ValidationException? Unique(UniqueConstraintViolationException unique, PersistFailureContext context)
        {
            if (string.Equals(unique.IndexName, OpenAssignmentIndex, StringComparison.OrdinalIgnoreCase))
            {
                return AtRoot(AssignmentsConcurrentCode, Arguments());
            }

            foreach (EntityMetadata entity in Entities(context.Root))
            {
                if (UniqueProperty(entity, unique.IndexName) is not PropertyMetadata property)
                {
                    continue;
                }

                bool blob = entity.BlobReferences.Any(reference => ReferenceEquals(reference.Property, property) || reference.Property.Name == property.Name);
                string code = blob ? ValidationCodes.BlobNotAttachable : ValidationCodes.Unique;
                ValidationPath? path = OnlyCarrier(context, entity, property, out _);
                return path is ValidationPath at
                    ? new ValidationException([new ValidationError(at, code, blob ? Arguments(("reason", PersistReason)) : Arguments())])
                    : AtRoot(code, blob ? Arguments(("reason", PersistReason), ("property", property.Name)) : Arguments(("property", property.Name)));
            }

            return null;
        }

        /// <summary>The property a unique index of an entity's table guards, as metadata declares it or the conventions name it.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="indexName">The index's name.</param>
        /// <returns>The property; null when the entity's table holds no such index.</returns>
        private static PropertyMetadata? UniqueProperty(EntityMetadata entity, string indexName)
        {
            // --- A declared index: the last of its properties, the scoping parent key leading it ---
            if (entity.UniqueIndexes.FirstOrDefault(index => string.Equals(index.IndexName, indexName, StringComparison.OrdinalIgnoreCase)) is UniqueIndexMetadata declared)
            {
                return declared.Properties[^1];
            }

            // --- A single-column index the conventions named ---
            return entity.Properties.FirstOrDefault(property =>
                string.Equals(ConstraintNames.UniqueIndex(entity.Table.Name, [property.Column]), indexName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Translates a foreign key violation of a saved row into the reference that names no row.</summary>
        /// <param name="foreignKey">The violation.</param>
        /// <param name="context">What the operation knows.</param>
        /// <returns>The exception to raise; null for a constraint the stack's tables do not hold.</returns>
        private static ValidationException? ForeignKey(ForeignKeyViolationException foreignKey, PersistFailureContext context)
        {
            foreach (EntityMetadata entity in Entities(context.Root))
            {
                ReferenceMetadata? reference = entity.References.FirstOrDefault(candidate =>
                    string.Equals(ConstraintNames.ForeignKey(entity.Table.Name, candidate.ForeignKey.Column), foreignKey.ConstraintName, StringComparison.OrdinalIgnoreCase));
                if (reference is null)
                {
                    continue;
                }

                // The one row carrying a value names it, widened as the validators name it; several
                // leave the value unknown.
                return OnlyCarrier(context, entity, reference.ForeignKey, out object? row) is ValidationPath at
                    ? new ValidationException([new ValidationError(at, ValidationCodes.FkNotFound, Arguments(("entity", reference.Target.Name), ("id", PayloadRows.ValueOf(reference.ForeignKey, row!))))])
                    : AtRoot(ValidationCodes.FkNotFound, Arguments(("entity", reference.Target.Name), ("property", reference.ForeignKey.Name)));
            }

            return null;
        }

        /// <summary>The path of the one payload row of an entity that carries a value of a property.</summary>
        /// <param name="context">What the operation knows.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="property">The property.</param>
        /// <param name="carrier">The row carrying the value; null when no single row carries one.</param>
        /// <returns>The path at the property; null when no row or several rows carry a value.</returns>
        private static ValidationPath? OnlyCarrier(PersistFailureContext context, EntityMetadata entity, PropertyMetadata property, out object? carrier)
        {
            carrier = null;
            ValidationPath? found = null;
            foreach (PersistRow row in context.Rows)
            {
                if (!ReferenceEquals(row.Entity, entity) || property.Getter(row.Row) is null)
                {
                    continue;
                }

                if (found is not null)
                {
                    carrier = null;
                    return null;
                }

                found = row.Path.Property(property.Name);
                carrier = row.Row;
            }

            return found;
        }

        /// <summary>An entity and every entity its collections hold, at every depth, each once.</summary>
        /// <param name="root">The root entity.</param>
        /// <returns>The entities, the root first.</returns>
        private static IEnumerable<EntityMetadata> Entities(EntityMetadata root)
        {
            HashSet<EntityMetadata> seen = new(ReferenceEqualityComparer.Instance);
            Stack<EntityMetadata> pending = new([root]);
            while (pending.TryPop(out EntityMetadata? entity))
            {
                if (!seen.Add(entity))
                {
                    continue;
                }

                yield return entity;
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    pending.Push(collection.Child);
                }
            }
        }

        /// <summary>A validation failure with one error at the payload's root.</summary>
        /// <param name="code">The error's code.</param>
        /// <param name="arguments">The error's arguments.</param>
        /// <returns>The exception.</returns>
        private static ValidationException AtRoot(string code, Dictionary<string, object?> arguments)
        {
            return new ValidationException([new ValidationError(ValidationPath.Root, code, arguments)]);
        }

        /// <summary>An error's arguments.</summary>
        /// <param name="arguments">Each argument's name and value.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
        {
            return arguments.ToDictionary(static argument => argument.Name, static argument => argument.Value, StringComparer.Ordinal);
        }
    }

    /// <summary>What an operation knows about its persist, for the translation of the persist's failures.</summary>
    /// <param name="Resource">The stack's resource.</param>
    /// <param name="Action">The access action the persist writes under, such as <c>Save</c>.</param>
    /// <param name="Root">The stack's root entity, whose tables and child tables the persist writes.</param>
    /// <param name="Rows">The payload's rows at every depth, each with its path.</param>
    /// <param name="LanguageIndex">The request's 1-based content language, which a conflict's modifier is named in.</param>
    /// <param name="CarriesQueryText">Whether the request carries Queryex text the caller can change, so an arithmetic failure is theirs.</param>
    internal sealed record PersistFailureContext(
        string Resource, string Action, EntityMetadata Root, IReadOnlyList<PersistRow> Rows, int LanguageIndex, bool CarriesQueryText = false);

    /// <summary>One payload row of a persist.</summary>
    /// <param name="Entity">The row's entity.</param>
    /// <param name="Row">The row.</param>
    /// <param name="Path">The row's path in the payload.</param>
    internal readonly record struct PersistRow(EntityMetadata Entity, object Row, ValidationPath Path);
}
