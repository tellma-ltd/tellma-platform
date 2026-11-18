// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Ids;

namespace Tellma.Core.Blobs
{
    // spec 0017 §4.1: the attach rule, over the aggregate's rows at every depth.
    /// <summary>
    ///     The blob-reference columns of an aggregate, and the values a payload row newly attaches to
    ///     them: a non-null value on an inserted row, or a changed non-null value on an updated one,
    ///     at any depth of its child collections.
    /// </summary>
    internal static class BlobAttachments
    {
        /// <summary>The argument of <see cref="ValidationCodes.BlobNotAttachable" /> naming why the blob cannot be attached.</summary>
        public const string ReasonArgument = "reason";

        /// <summary>Each aggregate's columns, walked once per root: the metadata lives as long as the process's model.</summary>
        private static readonly ConditionalWeakTable<EntityMetadata, IReadOnlyList<BlobColumn>> ColumnsByRoot = [];

        /// <summary>
        ///     Every blob-reference column of an aggregate: the root's, then each child collection's,
        ///     depth first, each table's column once.
        /// </summary>
        /// <param name="root">The aggregate root's metadata.</param>
        /// <returns>The columns, in walk order.</returns>
        public static IReadOnlyList<BlobColumn> Columns(EntityMetadata root)
        {
            ArgumentNullException.ThrowIfNull(root);
            return ColumnsByRoot.GetValue(root, static root =>
            {
                List<BlobColumn> columns = [];
                Collect(root, columns, [], new HashSet<EntityMetadata>(ReferenceEqualityComparer.Instance));
                return columns.AsReadOnly();
            });
        }

        /// <summary>
        ///     Appends the values a payload row and its children newly attach, in payload order: the
        ///     row's columns, then each present child collection's rows, depth first. A child row is
        ///     inserted when its key is absent from the matching collection of the row's before image,
        ///     and compared column by column with the stored child otherwise.
        /// </summary>
        /// <param name="entity">The row's entity.</param>
        /// <param name="row">The payload row.</param>
        /// <param name="before">
        ///     The row as stored, with the collections the payload carries; null for an inserted row,
        ///     every non-null value of which is newly attached.
        /// </param>
        /// <param name="path">The row's validation path.</param>
        /// <param name="into">Receives the attached values.</param>
        public static void NewlyAttached(EntityMetadata entity, object row, object? before, ValidationPath path, List<BlobAttach> into)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(into);

            // --- The row's own columns: a non-null value that is new or differs from the stored one ---
            foreach (BlobReferenceMetadata reference in entity.BlobReferences)
            {
                if (reference.Property.Getter(row) is int id && (before is null || reference.Property.Getter(before) is not int stored || stored != id))
                {
                    into.Add(new BlobAttach(reference, id, path.Property(reference.Property.Name)));
                }
            }

            // --- Each collection the payload carries, its rows matched by key to the stored children ---
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                if (collection.Getter(row) is not { Count: > 0 } children)
                {
                    continue;
                }

                Dictionary<long, object> stored = [];
                foreach (object child in (before is null ? null : collection.Getter(before)) ?? [])
                {
                    _ = stored.TryAdd(PayloadRows.KeyOf(collection.Child, child), child);
                }

                ValidationPath collectionPath = path.Property(collection.Property);
                for (int index = 0; index < children.Count; index++)
                {
                    object child = children[index];
                    object? match = stored.GetValueOrDefault(PayloadRows.KeyOf(collection.Child, child));
                    NewlyAttached(collection.Child, child, match, collectionPath.Index(index), into);
                }
            }
        }

        // spec 0017 §4.4 and §10.1: the persist's re-check rolls the whole save back, so every value
        // the save newly attached was refused with it, for the reason the refusal names.
        /// <summary>
        ///     Counts the attaches a persist refused when its failure translated to a blob that is no
        ///     longer attachable: one refusal per value the payload newly attached.
        /// </summary>
        /// <param name="translated">The translated failure of the persist.</param>
        /// <param name="roots">The payload's roots: each one's entity, row, before image (null for an insert) and path.</param>
        /// <param name="metrics">The attach instrument; null when the composition registers none.</param>
        public static void CountPersistRefusal(
            Exception translated, IEnumerable<(EntityMetadata Entity, object Row, object? Before, ValidationPath Path)> roots, BlobAttachMetrics? metrics)
        {
            ArgumentNullException.ThrowIfNull(translated);
            ArgumentNullException.ThrowIfNull(roots);
            if (metrics is null || translated is not ValidationException refused || !refused.Errors.Any(IsPersistRefusal))
            {
                return;
            }

            List<BlobAttach> attached = [];
            foreach ((EntityMetadata entity, object row, object? before, ValidationPath path) in roots)
            {
                NewlyAttached(entity, row, before, path, attached);
            }

            foreach (BlobAttach attach in attached)
            {
                metrics.Rejected(attach.Reference.Kind, BlobTelemetryTagValues.AttachPersist);
            }
        }

        /// <summary>Whether an error is a blob reference the persist refused.</summary>
        /// <param name="error">The error.</param>
        /// <returns>True when it is.</returns>
        private static bool IsPersistRefusal(ValidationError error)
        {
            return error.Code == ValidationCodes.BlobNotAttachable
                && error.Arguments.TryGetValue(ReasonArgument, out object? reason)
                && reason is BlobTelemetryTagValues.AttachPersist;
        }

        /// <summary>Collects an entity's blob-reference columns, then its children's, depth first.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="columns">The columns collected so far.</param>
        /// <param name="seen">The table columns collected so far.</param>
        /// <param name="path">The entities on the way down, which a self-nesting model would revisit.</param>
        private static void Collect(EntityMetadata entity, List<BlobColumn> columns, HashSet<(TableName Table, string Column)> seen, HashSet<EntityMetadata> path)
        {
            if (!path.Add(entity))
            {
                return;
            }

            foreach (BlobReferenceMetadata reference in entity.BlobReferences)
            {
                if (seen.Add((entity.Table, reference.Property.Column)))
                {
                    columns.Add(new BlobColumn(entity, reference));
                }
            }

            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                Collect(collection.Child, columns, seen, path);
            }

            _ = path.Remove(entity);
        }
    }

    /// <summary>One blob-reference column of an aggregate.</summary>
    /// <param name="Entity">The entity whose table holds the column.</param>
    /// <param name="Reference">The column's reference: its property, kind and policy.</param>
    internal sealed record BlobColumn(EntityMetadata Entity, BlobReferenceMetadata Reference)
    {
        /// <summary>The table holding the column.</summary>
        public TableName Table => Entity.Table;

        /// <summary>The column's name.</summary>
        public string Column => Reference.Property.Column;

        /// <summary>The column's kind.</summary>
        public string Kind => Reference.Kind;
    }

    /// <summary>One value a payload newly attaches.</summary>
    /// <param name="Reference">The column's reference.</param>
    /// <param name="Id">The staged blob's id.</param>
    /// <param name="Path">The property's validation path.</param>
    internal sealed record BlobAttach(BlobReferenceMetadata Reference, int Id, ValidationPath Path);
}
