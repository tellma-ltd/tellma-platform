// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Trees;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Save
{
    // Spec 0015 §6.6 steps 1 to 6, in order, after round trip 1 and before any validator
    // continuation resumes: existence and the pre-check, early concurrency detection, ownership and
    // the prefix, child identity (§6.5), the references (§7.4), uniqueness, then cycles and depth.
    // Steps 1 and 2 throw; the others collect errors, nothing stopping at the first.
    /// <summary>One save of one stack: the ordered checks that follow round trip 1.</summary>
    internal sealed partial class SaveRun<TEntity, TKey>
    {
        /// <summary>The code of the early concurrency conflict.</summary>
        private const string ConflictCode = "concurrency-conflict";

        /// <summary>The separator a display-name prefix may not hold, which joins it to the name.</summary>
        private const string PrefixSeparator = " - ";

        /// <summary>The stamp's form on the wire: seven fractional digits and the offset, so it round-trips.</summary>
        private const string StampFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffffzzz";

        /// <summary>A user's name columns, in language order.</summary>
        private static readonly string[] NameColumns = ["Name", "Name2", "Name3"];

        /// <summary>The stored child rows the save deletes by synchronisation, by entity and id.</summary>
        private readonly HashSet<(EntityMetadata Entity, long Id)> _deleted = [];

        /// <summary>Runs the checks that follow round trip 1, once; the one-round-trip create runs them without it.</summary>
        /// <returns>A task that completes when the checks ran.</returns>
        /// <exception cref="NotFoundException">An update root is missing or hidden.</exception>
        /// <exception cref="ForbiddenException">An update root lies outside the save grant's filter.</exception>
        /// <exception cref="ConcurrencyException">An update root's stamp already differs from the stored one.</exception>
        /// <exception cref="StepUpRequiredException">A new or changed reference names a sensitive target, below the step-up bar.</exception>
        private async Task SettleAsync()
        {
            _settled = true;

            // --- The ids of a reservation that rode round trip 1 ---
            if (_deferred)
            {
                IdReservation reservation = _reservation
                    ?? throw new InvalidOperationException("A deferred reservation rides round trip 1, which composed none.");
                reservation.Assign(Payload);
            }

            // --- Step 1: existence, then the save grant's count ---
            CheckExistence();

            // --- The before images shaped as the payload: absent collections null, children matched by id ---
            foreach (SaveRow root in _roots)
            {
                if (root.Before is object before)
                {
                    Match(root, before);
                }
            }

            // --- Step 2: early concurrency detection ---
            await CheckStampsAsync().ConfigureAwait(false);

            // --- The before images become the validators', and answer their loads by id ---
            var images = new TEntity?[_entities.Count];
            foreach (SaveRow root in _roots)
            {
                if (root.Before is TEntity before)
                {
                    images[root.RootIndex] = before;
                    _loader.Prime<TEntity, TKey>(before);
                }
            }

            _context.BeforeImages = images;

            // --- Steps 3 and 4, the references, then steps 5 and 6 ---
            CheckOwnership();
            CheckChildIdentity();
            CheckReferences();
            CheckUniqueness();
            if (_tree is TreeCycleValidator<TEntity>.TreeCheck tree)
            {
                TreeCycleValidator<TEntity>.Report(tree, _context);
            }
        }

        // Spec 0015 §6.6 step 1 and §9.2: an update id absent from the before images is missing or
        // hidden alike (404, naming the ids); a short count under the save grant's filter is 403 with
        // no ids named.
        /// <summary>Matches each update root to its stored row, refusing the save when one is missing or outside the grant.</summary>
        /// <exception cref="NotFoundException">An update root is missing or hidden.</exception>
        /// <exception cref="ForbiddenException">An update root lies outside the save grant's filter.</exception>
        private void CheckExistence()
        {
            var stored = _stored.ToDictionary(static entity => long.CreateChecked(entity.Id));
            List<long> missing = [];
            int matched = 0;
            foreach (SaveRow root in _roots)
            {
                if (root.IsNew)
                {
                    continue;
                }

                if (stored.TryGetValue(root.OriginalId, out TEntity? before))
                {
                    root.Before = before;
                    matched++;
                }
                else
                {
                    missing.Add(root.OriginalId);
                }
            }

            if (missing.Count > 0)
            {
                throw new NotFoundException(_stack.Resource, [.. missing.Select(static id => id.ToString(CultureInfo.InvariantCulture))]);
            }

            if (_saveCount is int count && count < matched)
            {
                throw new ForbiddenException(ForbiddenCode, _stack.Resource, AccessActions.Save, ForbiddenReason.OutsideFilter);
            }
        }

        // Spec 0015 §6.5: a collection the payload omits is untouched and absent from the before
        // image; a present one matches its update children by id, the stored children it does not
        // name being deleted. An unmatched update child is reported by child identity.
        /// <summary>Shapes a stored row's collections as the payload row's, matching each update child to its stored row.</summary>
        /// <param name="owner">The payload row.</param>
        /// <param name="before">Its stored row.</param>
        private void Match(SaveRow owner, object before)
        {
            foreach (ChildCollectionMetadata collection in owner.Entity.Children)
            {
                if (collection.Getter(owner.Row) is null)
                {
                    RowRules.SetCollection(before, collection, null);
                    continue;
                }

                // --- The stored children by id; those the payload names are matched, the rest deleted ---
                var stored = (collection.Getter(before) ?? []).ToDictionary(child => PayloadRows.KeyOf(collection.Child, child));
                foreach (SaveRow child in owner.Children)
                {
                    if (ReferenceEquals(child.Collection, collection) && !child.IsNew && stored.Remove(child.OriginalId, out object? match))
                    {
                        child.Before = match;
                        Match(child, match);
                    }
                }

                foreach (object deleted in stored.Values)
                {
                    Delete(collection.Child, deleted);
                }
            }
        }

        /// <summary>Records a stored child the synchronisation deletes, and every child of it the round loaded.</summary>
        /// <param name="entity">The child's entity.</param>
        /// <param name="row">The stored child.</param>
        private void Delete(EntityMetadata entity, object row)
        {
            _ = _deleted.Add((entity, PayloadRows.KeyOf(entity, row)));
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                foreach (object child in collection.Getter(row) ?? [])
                {
                    Delete(collection.Child, child);
                }
            }
        }

        // Spec 0015 §6.6 step 2 and §8: under Check, a default stamp on an update is an error at the
        // stamp, and a stamp that already differs from the stored one fails the save now, before the
        // validators spend a round. The conflict names who changed the row, in the reader's language.
        /// <summary>Refuses an update root without a stamp, and fails the save on a stamp the stored row has moved past.</summary>
        /// <returns>A task that completes when the stamps were compared.</returns>
        /// <exception cref="ConcurrencyException">A stamp differs from the stored one.</exception>
        private async Task CheckStampsAsync()
        {
            if (_options.Concurrency != ConcurrencyMode.Check)
            {
                return;
            }

            // --- Each update root's echoed stamp against its stored one ---
            List<(long Id, DateTimeOffset Stamp, int ModifiedById)> conflicts = [];
            foreach (SaveRow root in _roots)
            {
                if (root.IsNew || root.Rules.Stamp is not PropertyMetadata stamp || root.Before is not TopLevelEntity<TKey> stored)
                {
                    continue;
                }

                DateTimeOffset? expected = _stamps[root.RootIndex];
                if (expected is not DateTimeOffset echoed || echoed == default)
                {
                    _errors.Add(root.At(stamp.Name), ValidationCodes.ConcurrencyStampRequired);
                }
                else if (echoed != stored.ModifiedAt)
                {
                    conflicts.Add((root.OriginalId, stored.ModifiedAt, stored.ModifiedById));
                }
            }

            if (conflicts.Count == 0)
            {
                return;
            }

            // --- The conflict, counted, with each modifier's name ---
            _operation.Metrics.CountConflicts(CrudMetrics.StampConflict, conflicts.Count);
            IReadOnlyDictionary<int, string?> names = await ModifierNamesAsync([.. conflicts.Select(static conflict => conflict.ModifiedById).Distinct()]).ConfigureAwait(false);
            throw new ConcurrencyException(
                ConflictCode,
                [.. conflicts.Select(conflict => new ConcurrencyConflict(
                    conflict.Id,
                    conflict.Stamp.ToString(StampFormat, CultureInfo.InvariantCulture),
                    conflict.ModifiedById,
                    names.GetValueOrDefault(conflict.ModifiedById),
                    IsMissing: false))]);
        }

        // Spec 0015 §8: the modifier's name in the reader's content language, falling back to the
        // primary name when that twin is empty. The early conflict is rare, so its names cost a round
        // trip of their own rather than a join on every update's images.
        /// <summary>Loads the names of the users who last modified the conflicting rows.</summary>
        /// <param name="userIds">The users' ids.</param>
        /// <returns>Each user's name in the reader's language; empty when the tenant's schema holds no users.</returns>
        private async Task<IReadOnlyDictionary<int, string?>> ModifierNamesAsync(int[] userIds)
        {
            // --- The name columns the tenant's schema declares ---
            if (_map.Schema.FindEntity(CoreResources.User) is not EntityDescriptor users)
            {
                return new Dictionary<int, string?>();
            }

            string[] columns = [.. NameColumns.Where(name => users.Properties.Any(property => property.Name == name))];
            if (columns.Length == 0)
            {
                return new Dictionary<int, string?>();
            }

            // --- One read of the users, unfiltered: a conflict names its modifier whoever reads it ---
            BatchResult<RowPage>? page = null;
            QueryRowSet rows = await _operation.Runner.RunAsync(
                BatchPurpose.Validate,
                (_, batch) =>
                {
                    batch.Limits = QueryexProfiles.PipelineLimits;
                    string key = users.Key.Name;
                    page = batch.Rows(
                        new QuerySpec
                        {
                            Root = users,
                            Select = $"{key},{string.Join(',', columns)}",
                            Restrictions = [new KeySetRestriction(key, KeyTables.Bind(batch, userIds).Name)],
                        },
                        arguments: null,
                        options: null);
                    return ValueTask.CompletedTask;
                },
                (_, _) => page!.Value.Rows,
                _cancellationToken).ConfigureAwait(false);
            RequestContext current = _operation.Accessor.Current;
            current.ApplyCultures();
            int language = current.ContentLanguageIndex;

            // --- Each user's name in the reader's language, else the primary one ---
            Dictionary<int, string?> names = new(rows.RowCount);
            Array ids = rows.GetBuffer(0);
            for (int row = 0; row < rows.RowCount; row++)
            {
                string? primary = Text(rows, 1, row);
                int twin = Array.IndexOf(columns, language == 1 ? "Name" : $"Name{language}");
                string? localized = twin > 0 ? Text(rows, twin + 1, row) : null;
                names[Convert.ToInt32(ids.GetValue(row), CultureInfo.InvariantCulture)] = string.IsNullOrEmpty(localized) ? primary : localized;
            }

            return names;
        }

        /// <summary>A text cell of a loaded row set.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="column">The column.</param>
        /// <param name="row">The row.</param>
        /// <returns>The text, or null.</returns>
        private static string? Text(QueryRowSet rows, int column, int row)
        {
            return rows.IsNull(column, row) ? null : (string?)rows.GetBuffer(column).GetValue(row);
        }

        // Spec 0015 §6.2 and §6.6 step 3: an update's server-owned values come from its stored row;
        // a write-once value the client left empty keeps the stored one, and one that differs is an
        // error; a text prefix may not hold the separator a display name splits at.
        /// <summary>Restores the server-owned values of the matched rows, guards the write-once ones and checks the prefixes.</summary>
        private void CheckOwnership()
        {
            foreach (SaveRow row in _rows)
            {
                object entity = row.Row;
                if (row.Before is object before)
                {
                    // --- Server-owned, then write-once, against the stored row ---
                    foreach (PropertyMetadata property in row.Rules.ServerOwned)
                    {
                        property.Setter(entity, property.Getter(before));
                    }

                    foreach (PropertyMetadata property in row.Rules.WriteOnce)
                    {
                        object? value = property.Getter(entity);
                        object? stored = property.Getter(before);
                        if (IsDefault(value))
                        {
                            property.Setter(entity, stored);
                        }
                        else if (!Equals(value, stored))
                        {
                            _errors.Add(row.At(property.Name), ValidationCodes.WriteOnce);
                        }
                    }
                }

                // --- The prefix, on every row ---
                if (row.Rules.Prefix is PropertyMetadata prefix && prefix.Getter(entity) is string text && text.Contains(PrefixSeparator, StringComparison.Ordinal))
                {
                    _errors.Add(row.At(prefix.Name), ValidationCodes.DisplayNamePrefixSeparator, Arguments(("property", prefix.Name), ("separator", PrefixSeparator)));
                }
            }
        }

        /// <summary>Whether a value is its type's default: null, or a value type's zero.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsDefault(object? value)
        {
            return value is null || (value.GetType().IsValueType && value.Equals(RuntimeHelpers.GetUninitializedObject(value.GetType())));
        }

        // Spec 0015 §6.5: a child id above zero that is not among its parent's stored children
        // belongs to another parent, to a hidden one or to nothing, and is never re-parented or
        // inserted. A child of an unmatched child is not judged again: its owner is reported.
        /// <summary>Refuses each update child its parent's stored children do not hold.</summary>
        private void CheckChildIdentity()
        {
            foreach (SaveRow row in _rows)
            {
                if (row.Owner is not SaveRow owner || row.IsNew || row.Before is not null || owner is { IsNew: false, Before: null })
                {
                    continue;
                }

                _errors.Add(row.At(row.Entity.Key.Name), ValidationCodes.EntityNotFound);
            }
        }

        // Spec 0015 §7.4: a new or changed reference names a row its target holds under the caller's
        // read decision, or it is not found at the property. Judging one tells the caller whether a
        // row exists, so a target read under a sensitive securable asks for the step-up a direct read
        // of it asks for (spec 0014 §4.3) before any reference to it is judged; an unchanged
        // reference is not judged and asks nothing, and neither does the platform's own work.
        /// <summary>Refuses each new or changed reference whose row the caller cannot see.</summary>
        /// <exception cref="StepUpRequiredException">A new or changed reference names a sensitive target, below the step-up bar.</exception>
        /// <exception cref="IndividualRequiredException">The same, for a service account, which cannot step up.</exception>
        private void CheckReferences()
        {
            foreach ((SaveRow row, ReferenceMetadata reference, long value) in _references)
            {
                // An update row that matched no stored row is reported already.
                if (!row.IsNew && row.Before is null)
                {
                    continue;
                }

                if (row.Before is object before && PayloadRows.ValueOf(reference.ForeignKey, before) == value)
                {
                    continue;
                }

                // --- The step-up of a sensitive target, as a direct read of it asks for it ---
                if (_sensitive.Contains(reference.Target))
                {
                    EnsureStepUp();
                }

                if (!(_visible.TryGetValue(reference.Target, out HashSet<long>? visible) && visible.Contains(value)))
                {
                    _errors.Add(row.At(reference.ForeignKey.Name), ValidationCodes.FkNotFound, Arguments(("entity", reference.Target.Name), ("id", value)));
                }
            }
        }

        // Spec 0015 §6.6 step 5: duplicates within the payload, scoped by parent for a property
        // unique within its parent, then values a stored row already holds, except where the
        // persist batch's order frees the value first. The persist inserts the new roots before it
        // updates the existing ones, and synchronises children by deleting, then updating, then
        // inserting. So a stored holder does not count when it is the row itself; when it is a
        // child the synchronisation deletes; or when the payload updates it to another value
        // (after ownership restored what the server keeps) and the row taking the value is an
        // update of the same statement or a child. A new root taking a value an updated root gives
        // up would meet the stored value in the insert, so that holder counts. One error per row
        // and property.
        /// <summary>Refuses the payload's duplicate values, and the values stored rows already hold.</summary>
        private void CheckUniqueness()
        {
            foreach (UniqueProbe probe in _probes)
            {
                // The payload's update rows of the probe's entity that matched a stored row, by id:
                // what each holds after ownership. An unmatched one updates nothing (it is reported).
                Dictionary<long, SaveRow> updated = [];
                foreach (SaveRow row in _rows)
                {
                    if (ReferenceEquals(row.Rules, probe.Rules) && !row.IsNew && row.Before is not null)
                    {
                        _ = updated.TryAdd(row.OriginalId, row);
                    }
                }

                bool child = !ReferenceEquals(probe.Rules.Entity, _root);
                HashSet<SaveRow> flagged = [];

                // --- Within the payload, by scope then value ---
                Dictionary<long, Dictionary<object, List<SaveRow>>> scopes = [];
                foreach ((SaveRow row, object value) in probe.Holders)
                {
                    long scope = probe.ParentKey is null ? 0 : row.Owner?.Id ?? 0;
                    if (!scopes.TryGetValue(scope, out Dictionary<object, List<SaveRow>>? values))
                    {
                        scopes[scope] = values = new Dictionary<object, List<SaveRow>>(probe.Comparer);
                    }

                    if (!values.TryGetValue(value, out List<SaveRow>? holders))
                    {
                        values[value] = holders = [];
                    }

                    holders.Add(row);
                }

                foreach (List<SaveRow> holders in scopes.Values.SelectMany(static values => values.Values).Where(static holders => holders.Count > 1))
                {
                    foreach (SaveRow row in holders)
                    {
                        _errors.Add(row.At(probe.Property.Name), ValidationCodes.Unique);
                        _ = flagged.Add(row);
                    }
                }

                // --- Against the stored rows ---
                foreach ((SaveRow row, object value) in probe.Holders)
                {
                    if (flagged.Contains(row))
                    {
                        continue;
                    }

                    long id = row.Id;
                    long? scope = probe.ParentKey is null ? null : row.Owner?.Id;
                    bool freesFirst = child || !row.IsNew;
                    bool taken = probe.Loaded(value).Any(stored =>
                        stored.Id != id
                        && !_deleted.Contains((probe.Rules.Entity, stored.Id))
                        && (probe.ParentKey is null || stored.ParentId == scope)
                        && !(freesFirst && GivesUp(updated, stored.Id, probe, value)));
                    if (taken)
                    {
                        _errors.Add(row.At(probe.Property.Name), ValidationCodes.Unique);
                    }
                }
            }
        }

        /// <summary>Whether the payload updates a stored row away from a value: the row's value after ownership is another one.</summary>
        /// <param name="updated">The payload's update rows of the probe's entity, by id.</param>
        /// <param name="id">The stored row's id.</param>
        /// <param name="probe">The uniqueness check, whose comparer compares the values.</param>
        /// <param name="value">The value the stored row holds.</param>
        /// <returns>True when the payload updates the row and its value no longer matches.</returns>
        private static bool GivesUp(Dictionary<long, SaveRow> updated, long id, UniqueProbe probe, object value)
        {
            return updated.TryGetValue(id, out SaveRow? holder)
                && !(probe.Property.Getter(holder.Row) is object held && probe.Comparer.Equals(held, value));
        }
    }
}
