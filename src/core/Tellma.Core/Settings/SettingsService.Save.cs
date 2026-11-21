// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud;
using Tellma.Core.Data;
using Tellma.Core.Data.Conventions;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Settings
{
    // The save is spec 0013 §6.3, in two round trips through the runner. The validate batch reads the
    // typed row (only when a typed member is listed) and the listed entries, and reserves the ids of
    // the entries a save may insert, since a cold id buffer appends its statement to that batch. The
    // merge runs in memory and writes nothing when it refuses. The persist batch saves the typed
    // row, saves and deletes the entries, moves the permissions tag and every list's tag on a
    // language change, and ends with the closing reads the response and the warmed store are built
    // from. The names are mirrored to the catalog after the commit.
    /// <content>The save of a settings patch.</content>
    internal sealed partial class SettingsService
    {
        /// <summary>The unique index on the entries' key, which a concurrent first write of a key violates.</summary>
        private static readonly string EntryKeyIndex = ConstraintNames.UniqueIndex("SettingEntries", [nameof(SettingEntry.Key)]);

        /// <summary>
        ///     Saves a patch of the typed settings and the entries, each row checked against the stamp
        ///     the patch expects unless it overrides, and answers both views as committed.
        /// </summary>
        /// <param name="patch">The patch: the members and entries it lists, their values, and the stamps it expects.</param>
        /// <param name="cancellationToken">Cancels the save before its persist is sent.</param>
        /// <returns>The editor's view, the client's view and the client view's new wire tag.</returns>
        /// <exception cref="ForbiddenException">The caller may not save a listed member's category, or, for a patch that names none, any category.</exception>
        /// <exception cref="ValidationException">The patch names something unknown, or a value it sets is refused.</exception>
        /// <exception cref="ConcurrencyException">A row's stamp is not the one the patch expects.</exception>
        /// <exception cref="TenantUnavailableException">The tenant's state admits no persist.</exception>
        [ApiAction("save", MemberOnly = true, Mcp = McpExposure.Hidden)]
        public async Task<SettingsSaveResult> SaveAsync(TenantSettingsPatch patch, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(patch);
            var fields = SettingsPatchFields.Of(patch);

            // --- Step 1: every listed category's save grant, the typed row's carrying its step-up ---
            await AuthorizeSaveAsync(fields, cancellationToken).ConfigureAwait(false);

            // --- Step 2: the validate read, and the ids of the entries the save may insert ---
            Dictionary<string, SettingEntry> placeholders = Placeholders(patch, fields);
            IdReservation? reservation = null;
            try
            {
                (StoredSettings? typed, FrozenDictionary<string, StoredEntry> entries) = await ValidateReadAsync(
                    fields, placeholders, value => reservation = value, () => reservation, cancellationToken).ConfigureAwait(false);
                reservation?.Assign([.. placeholders.Values]);

                // --- Step 3: the merge, which refuses before anything is written ---
                RequestContext context = accessor.Current;
                int tenantId = context.RequireTenant().Id;
                IDataProtector? protector = protection is null ? null : SettingSecrets.For(protection, tenantId);
                SettingsMerge merge = merger.Merge(patch, fields, typed, entries, protector, context.ContentLanguageIndex);

                // --- Step 4: the persist and its closing reads; step 6's mirror after its commit ---
                PersistVerdict.Ensure(context, mutation: true, allowWhenReadOnly: false, frames.Open);
                SettingsPersist persisted = await PersistAsync(patch, merge, placeholders, tenantId, cancellationToken).ConfigureAwait(false);

                // --- Step 5: the writer warmed from the closing reads, and both views answered ---
                VersionTag tag = persisted.Outcome.VersionTags[VersionTagNames.Settings];
                TenantSettingsEntry entry = store.Build(tenantId, allowPlaceholder: accessor.Current.IsSystem, tag, SettingsReads.ColdLoadRows(persisted.Typed, persisted.Entries));
                store.Offer(tenantId, entry, tag);
                TenantSettingsDetails details = persisted.Reads.Details(this, persisted.Held, persisted.Typed, persisted.Entries);
                await MirroredAsync(persisted.Mirror, tenantId).ConfigureAwait(false);
                return new SettingsSaveResult(details, entry.Client, VersionTagWire.Settings(tag));
            }
            finally
            {
                // Gives the ids back when no persist was sent; releases nothing once one was.
                reservation?.Dispose();
            }
        }

        /// <summary>
        ///     Requires the save grant of every resource the patch touches: the typed row's when it
        ///     lists a typed member, and each listed declared key's category's, the general category
        ///     sharing the typed row's. An undeclared key is refused by the merge, not here; a mask
        ///     that names no typed member and no declared key needs any one settings save grant.
        /// </summary>
        /// <param name="fields">The sorted field mask.</param>
        /// <param name="cancellationToken">Cancels the evaluation.</param>
        /// <returns>A task that completes when every grant is held.</returns>
        /// <exception cref="ForbiddenException">A grant is not held, or its step-up is not met.</exception>
        private async Task AuthorizeSaveAsync(SettingsPatchFields fields, CancellationToken cancellationToken)
        {
            List<string> resources = [];
            if (fields.ListsTyped)
            {
                resources.Add(CoreResources.SettingsGeneral);
            }

            foreach (string name in fields.Keys)
            {
                if (keys.Find(name) is SettingKey key)
                {
                    string category = SettingKeyRegistry.CategoryOf(key);
                    resources.Add(SettingKeyRegistry.IsGeneral(category) ? CoreResources.SettingsGeneral : CoreResources.SettingsPrefix + category);
                }
            }

            // --- A mask that names no resource still needs a settings save grant: any one will do ---
            if (resources.Count == 0)
            {
                SecurableRef[] securables = [.. Resources().Select(static resource => new SecurableRef(resource, AccessActions.Save))];
                IReadOnlyList<AccessDecision> decisions = await evaluator.EvaluateAsync(securables, cancellationToken).ConfigureAwait(false);
                if (!decisions.Any(static decision => decision.IsAllowed))
                {
                    throw new ForbiddenException(ForbiddenCode, CoreResources.SettingsGeneral, AccessActions.Save, ForbiddenReason.NoGrant);
                }

                return;
            }

            foreach (string resource in resources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _ = await evaluator.RequireAsync(resource, AccessActions.Save, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The typed row's resource and every category's, each once.</summary>
        /// <returns>The resources.</returns>
        private IEnumerable<string> Resources()
        {
            return new[] { CoreResources.SettingsGeneral }
                .Concat(keys.Categories.Select(static category => category.Resource))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     The rows the save may insert, one per listed declared key that sets a value and either
        ///     expects no row or overrides the stamps; given ids by the validate batch's reservation.
        /// </summary>
        /// <param name="patch">The patch.</param>
        /// <param name="fields">The sorted field mask.</param>
        /// <returns>The rows, by key.</returns>
        private Dictionary<string, SettingEntry> Placeholders(TenantSettingsPatch patch, SettingsPatchFields fields)
        {
            Dictionary<string, SettingEntry> rows = new(StringComparer.Ordinal);
            foreach (string name in fields.Keys)
            {
                bool sets = patch.Entries is not null
                    && patch.Entries.TryGetValue(name, out System.Text.Json.JsonElement? value)
                    && value is { ValueKind: not (System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined) };
                bool expectsNone = patch.Concurrency == ConcurrencyMode.Override
                    || patch.ExpectedEntryStamps is null
                    || !patch.ExpectedEntryStamps.TryGetValue(name, out DateTimeOffset? expected)
                    || expected is null;
                if (sets && expectsNone && keys.Find(name) is not null)
                {
                    rows.Add(name, new SettingEntry { Key = name, Value = string.Empty });
                }
            }

            return rows;
        }

        /// <summary>
        ///     Runs the validate batch: the typed row when a typed member is listed, the listed
        ///     entries, and the reservation of the placeholders' ids, made afresh on a recompose.
        ///     Nothing listed reads nothing, with no round trip.
        /// </summary>
        /// <param name="fields">The sorted field mask.</param>
        /// <param name="placeholders">The rows the save may insert.</param>
        /// <param name="reserved">Receives each reservation made.</param>
        /// <param name="current">The reservation made last.</param>
        /// <param name="cancellationToken">Cancels the batch.</param>
        /// <returns>The typed row (null when not read) and the listed entries' stored rows.</returns>
        private async Task<(StoredSettings? Typed, FrozenDictionary<string, StoredEntry> Entries)> ValidateReadAsync(
            SettingsPatchFields fields,
            Dictionary<string, SettingEntry> placeholders,
            Action<IdReservation?> reserved,
            Func<IdReservation?> current,
            CancellationToken cancellationToken)
        {
            if (!fields.ListsTyped && fields.Keys.Count == 0)
            {
                return (null, FrozenDictionary<string, StoredEntry>.Empty);
            }

            BatchResult<RawResult>? typedRead = null;
            BatchResult<RawResult>? entriesRead = null;
            return await runner.RunAsync(
                BatchPurpose.Validate,
                (user, batch) =>
                {
                    typedRead = fields.ListsTyped ? SettingsReads.TypedRow(batch) : null;
                    entriesRead = fields.Keys.Count > 0 ? SettingsReads.Entries(batch, fields.Keys) : null;

                    // A recompose reserves afresh on the new batch; the previous one was never sent.
                    current()?.Dispose();
                    reserved(placeholders.Count > 0 ? allocator.Reserve(batch, typeof(SettingEntry), [.. placeholders.Values]) : null);
                    return ValueTask.CompletedTask;
                },
                (_, _) => (
                    typedRead is null ? null : SettingsReads.ReadTypedRow(typedRead),
                    entriesRead is null ? FrozenDictionary<string, StoredEntry>.Empty : SettingsReads.ReadEntries(entriesRead)),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Runs the persist batch: the typed row, the entries' saves and deletes, the tags a
        ///     language change moves, and the closing reads of the categories the composing user
        ///     reads. A patch that writes nothing runs the closing reads alone, on a Read batch. A
        ///     commit the probe proved after its response was lost is answered from the closing reads
        ///     run again on their own.
        /// </summary>
        /// <param name="patch">The patch.</param>
        /// <param name="merge">What it writes.</param>
        /// <param name="placeholders">The rows with reserved ids, by key.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the batch before it is sent.</param>
        /// <returns>The committed batch's outcome and closing reads, and the mirror started after its commit.</returns>
        /// <exception cref="ConcurrencyException">A row's stamp moved, a row was deleted, or a concurrent save inserted a listed key.</exception>
        private async Task<SettingsPersist> PersistAsync(
            TenantSettingsPatch patch,
            SettingsMerge merge,
            Dictionary<string, SettingEntry> placeholders,
            int tenantId,
            CancellationToken cancellationToken)
        {
            // --- The entries' writes: the changed rows with their stored stamps, the new ones with their reserved ids ---
            ConcurrencyMode concurrency = patch.Concurrency;
            List<SettingEntry> saves = [.. merge.Updates];
            foreach ((string key, string value) in merge.Inserts)
            {
                SettingEntry row = placeholders[key];
                row.Value = value;
                saves.Add(row);
            }

            bool writes = merge.Typed is not null || saves.Count > 0 || merge.Deletes.Count > 0;
            EditorReads? reads = null;
            HeldCategories? held = null;
            Task? mirror = null;
            BatchOutcome outcome;
            try
            {
                outcome = await runner.RunAsync(
                    writes ? BatchPurpose.Persist : BatchPurpose.Read,
                    (user, batch) =>
                    {
                        // --- The typed row, carrying the stamp the emitter compares ---
                        if (merge.Typed is TypedSettings typed)
                        {
                            _ = batch.Save([typed], concurrency);
                        }

                        // --- The entries: saved, then the reset ones deleted with their hydrated stamps ---
                        if (saves.Count > 0)
                        {
                            _ = batch.Save(saves, concurrency);
                        }

                        if (merge.Deletes.Count > 0)
                        {
                            _ = batch.Delete(new DeleteSpec<SettingEntry>.ByIds(
                                [.. merge.Deletes.Select(static row => (object)row.Id)],
                                ReadFilter: null,
                                Filter: null,
                                concurrency == ConcurrencyMode.Check ? [.. merge.Deletes.Select(static row => (DateTimeOffset?)row.ModifiedAt)] : null));
                        }

                        // --- A language change: the query schema's shape and every cached list's columns follow the languages ---
                        if (merge.LanguagesChanged)
                        {
                            batch.BumpVersionTag(VersionTagNames.Permissions);
                            foreach (string name in versionTags.Names.Where(static name => name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal)))
                            {
                                batch.BumpVersionTag(name);
                            }
                        }

                        // --- The closing reads, which end the batch, of the categories the user each
                        // composition is for reads (spec 0015 §5.1) ---
                        held = HeldBy(user);
                        reads = ComposeEditorReads(batch, held, typed: true, merge.Changes);

                        // --- The mirror, started once the batch committed ---
                        if (merge.Mirror is TenantDisplayNames names)
                        {
                            batch.OnCommitted(_ => mirror = MirrorAsync(tenantId, names));
                        }

                        return ValueTask.CompletedTask;
                    },
                    static (_, outcome) => outcome,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // Spec 0015 §8: the probe found an inserted entry, so the save committed and only its
                // response was lost. Its closing reads run again on their own, and the mirror the
                // commit would have started starts now.
                (outcome, reads, held) = await ClosingReadsAsync(merge.Changes).ConfigureAwait(false);
                if (merge.Mirror is TenantDisplayNames names)
                {
                    mirror = MirrorAsync(tenantId, names);
                }
            }
            catch (DataAccessException failure) when (Translate(failure) is ConcurrencyException translated)
            {
                throw translated;
            }

            // --- The closing reads, read once for the store and the response ---
            accessor.Current.ApplyCultures();
            EditorReads closing = reads ?? throw new InvalidOperationException("A committed settings persist composed its closing reads.");
            return new SettingsPersist(outcome, closing, held!, closing.TypedRow, SettingsReads.ReadEntries(closing.Entries), mirror);
        }

        /// <summary>Runs a committed save's closing reads on a Read batch of their own, whatever the caller's token says.</summary>
        /// <param name="changes">The stored JSON of the entries the save wrote, by key, null for a reset one.</param>
        /// <returns>The read batch's outcome, its closing reads, and the categories they cover.</returns>
        private async Task<(BatchOutcome Outcome, EditorReads Reads, HeldCategories Held)> ClosingReadsAsync(IReadOnlyDictionary<string, string?> changes)
        {
            EditorReads? reads = null;
            HeldCategories? held = null;
            BatchOutcome outcome = await runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    held = HeldBy(user);
                    reads = ComposeEditorReads(batch, held, typed: true, changes);
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome,
                CancellationToken.None).ConfigureAwait(false);
            return (outcome, reads!, held!);
        }

        /// <summary>
        ///     Translates a persist's failure: a stale or deleted row, and a concurrent first write of
        ///     a listed key, are concurrency conflicts; a deleted row is never a not-found here, since
        ///     the editor read it.
        /// </summary>
        /// <param name="failure">What the persist threw.</param>
        /// <returns>The conflict to raise instead; null for a failure that passes through.</returns>
        private ConcurrencyException? Translate(DataAccessException failure)
        {
            // The runner may have refreshed the context: the modifier is named in the request's language.
            accessor.Current.ApplyCultures();
            int language = accessor.Current.ContentLanguageIndex;
            return failure switch
            {
                ConcurrencyConflictException conflict => new ConcurrencyException(
                    PersistFailures.ConflictCode,
                    [.. conflict.Conflicts.Select(row => row.IsMissing || row.ModifiedAt is not DateTimeOffset stamp
                        ? new ConcurrencyConflict(row.Id, null, null, null, IsMissing: true)
                        : SettingsPatchMerger.Conflict(row.Id, stamp, row.ModifiedById ?? 0, row.Name is null ? null : new StoredModifier(row.Name, row.Name2, row.Name3), language))]),
                UniqueConstraintViolationException unique when string.Equals(unique.IndexName, EntryKeyIndex, StringComparison.OrdinalIgnoreCase) =>
                    new ConcurrencyException(PersistFailures.ConflictCode, [new ConcurrencyConflict(0, null, null, null, IsMissing: false)]),
                BatchAssertionFailedException assertion when assertion.Number == TellmaSqlErrors.NotFound =>
                    new ConcurrencyException(PersistFailures.ConflictCode, [.. assertion.MissingIds.Select(static id => new ConcurrencyConflict(id, null, null, null, IsMissing: true))]),
                _ => null,
            };
        }

        /// <summary>Mirrors the names to the catalog; a failure is logged, never surfaced.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="names">The names.</param>
        /// <returns>A task that completes when the mirror ran, whatever its outcome.</returns>
        private async Task MirrorAsync(int tenantId, TenantDisplayNames names)
        {
            if (catalog is null)
            {
                return;
            }

            try
            {
                // The save committed: the mirror runs whatever the caller's token says.
                await catalog.RenameAsync(tenantId, names, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                SettingsLog.SettingsNamesMirrorFailed(logger, failure, tenantId);
            }
        }

        /// <summary>Waits for a started mirror, which never fails.</summary>
        /// <param name="mirror">The mirror; null when none was started.</param>
        /// <param name="tenantId">The tenant, for the log.</param>
        /// <returns>A task that completes when the mirror ran.</returns>
        private async Task MirroredAsync(Task? mirror, int tenantId)
        {
            if (mirror is null)
            {
                return;
            }

            try
            {
                await mirror.ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // The mirror logs its own failures; this is a failure to start it.
                SettingsLog.SettingsNamesMirrorFailed(logger, failure, tenantId);
            }
        }

        /// <summary>A committed persist: its outcome, its closing reads, the categories and rows they read, and the mirror it started.</summary>
        /// <param name="Outcome">The batch's outcome, or that of the read that answered a commit whose response was lost.</param>
        /// <param name="Reads">The closing reads.</param>
        /// <param name="Held">The categories the closing reads cover, as the composing user's set grants them.</param>
        /// <param name="Typed">The typed row read.</param>
        /// <param name="Entries">Every stored entry read.</param>
        /// <param name="Mirror">The mirror started after the commit; null when none was.</param>
        private sealed record SettingsPersist(BatchOutcome Outcome, EditorReads Reads, HeldCategories Held, StoredSettings? Typed, FrozenDictionary<string, StoredEntry> Entries, Task? Mirror);
    }
}
