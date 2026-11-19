// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Caching;
using Tellma.Core.Crud;
using Tellma.Core.Tenancy;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Settings
{
    // The service is spec 0013 §6: the client's view and the entity tags behind the prologue, the
    // editor's view from the rows themselves, the save of a patch in two round trips, and the
    // refresh of every tag. Every batch runs through the guarded runner, so a moved settings or
    // permissions tag recomposes it under the fresh context (§2.6).
    /// <summary>The tenant settings' routed actions: the client's view, the entity tags, the editor's view, the save and the refresh of every cache.</summary>
    /// <param name="runner">Runs every batch behind the connect prologue.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="settingsCache">The scope's view of the settings store.</param>
    /// <param name="cacheable">The scope's view of the cacheable entities.</param>
    /// <param name="evaluator">The scope's access evaluator.</param>
    /// <param name="keys">The setting keys.</param>
    /// <param name="merger">Merges and validates a patch.</param>
    /// <param name="schemas">The query schemas the related reads project under.</param>
    /// <param name="stacks">The stack registry, whose stacks state the related selects.</param>
    /// <param name="metadata">The entity metadata of the referenced types.</param>
    /// <param name="allocator">Reserves the ids of new entries.</param>
    /// <param name="store">The process's settings store, which a save warms.</param>
    /// <param name="versionTags">The registered version tags, every one of which a refresh moves.</param>
    /// <param name="publisher">Publishes the client events.</param>
    /// <param name="frames">The write frames the scope's persists are nested in.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="protection">The host's data protection; null in a host without it, which saves no secret.</param>
    /// <param name="catalog">The tenant catalog the names are mirrored to; null in a host without it, which mirrors nothing.</param>
    [ApiRoute("settings")]
    internal sealed partial class SettingsService(
        IGuardedBatchRunner runner,
        IRequestContextAccessor accessor,
        ITenantSettingsCache settingsCache,
        ICacheableEntities cacheable,
        IAccessEvaluator evaluator,
        ISettingKeyRegistry keys,
        SettingsPatchMerger merger,
        IQueryexSchemaProvider schemas,
        IStackRegistry stacks,
        IEntityMetadataProvider metadata,
        IIdAllocator allocator,
        TenantSettingsStore store,
        IVersionTagRegistry versionTags,
        IClientEventPublisher publisher,
        WriteFrames frames,
        ILogger<SettingsService> logger,
        IDataProtectionProvider? protection = null,
        ITenantCatalog? catalog = null)
    {
        /// <summary>The code of a refusal for want of a grant.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>The tables a refresh's raw statement writes.</summary>
        private static readonly FrozenSet<TableName> UserStampsWritten = new[] { UserStampConfiguration.Table }.ToFrozenSet();

        /// <summary>The related reads of the reference keys.</summary>
        private readonly SettingsRelatedReads _related = new(schemas, stacks, metadata);

        /// <summary>
        ///     The client's view of the tenant's settings, pre-serialized, and its wire tag. Every
        ///     member reads it: it carries only the keys visible to the client.
        /// </summary>
        /// <param name="cancellationToken">Cancels the prologue.</param>
        /// <returns>The view's bytes and wire tag.</returns>
        [ApiAction("client", MemberOnly = true, Idempotent = true, Mutation = false)]
        public async Task<(byte[] Json, string WireTag)> ClientAsync(CancellationToken cancellationToken = default)
        {
            // The prologue keeps the snapshot current; a warm instance adds no statement to it.
            await PrologueAsync(cancellationToken).ConfigureAwait(false);
            return await settingsCache.GetForClientAsync().ConfigureAwait(false);
        }

        /// <summary>The wire tag of every cacheable entity's list, by entity name.</summary>
        /// <param name="cancellationToken">Cancels the prologue.</param>
        /// <returns>The tags.</returns>
        [ApiAction("entity-tags", MemberOnly = true, Idempotent = true, Mutation = false)]
        public async Task<IReadOnlyDictionary<string, string>> EntityTagsAsync(CancellationToken cancellationToken = default)
        {
            await PrologueAsync(cancellationToken).ConfigureAwait(false);
            return await cacheable.GetWireTagsAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///     The settings editor's view of every category the caller may read: the typed row, each
        ///     key's value, which keys are stored, each stored row's stamp, and the rows the reference
        ///     keys point at.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The view.</returns>
        /// <exception cref="ForbiddenException">The caller may read no category.</exception>
        [ApiAction("details", MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
        public async Task<TenantSettingsDetails> DetailsAsync(CancellationToken cancellationToken = default)
        {
            // --- One evaluation over the typed row and every category; none held is a refusal ---
            HeldCategories held = await HeldAsync(cancellationToken).ConfigureAwait(false);
            if (!held.Any)
            {
                throw new ForbiddenException(ForbiddenCode, CoreResources.SettingsGeneral, AccessActions.Read, ForbiddenReason.NoGrant);
            }

            // --- One Read batch: the rows themselves and the related reads of the cached values ---
            EditorReads? reads = null;
            return await runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    reads = ComposeEditorReads(batch, held, typed: held.General, overlay: null);
                    return ValueTask.CompletedTask;
                },
                (_, _) => reads!.Details(this, held),
                cancellationToken).ConfigureAwait(false);
        }

        // Spec 0013 §6.4: one Persist batch bumps every registered tag, which the guard and the
        // epilogue handle as for any bump, and moves both user-level tags of every user in one raw
        // statement. The epilogue announces the tenant-level names; this action adds the users'
        // preferences, with no value, since every user's moved.
        /// <summary>
        ///     Moves every version tag of the tenant and both user-level tags of every user, so every
        ///     instance and every user reloads what it caches: for use after out-of-band changes.
        /// </summary>
        /// <param name="cancellationToken">Cancels the persist before it is sent.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        /// <exception cref="TenantUnavailableException">The tenant's state admits no persist.</exception>
        [ApiAction("refresh-caches", Resource = CoreResources.SettingsGeneral, Action = AccessActions.Save, Idempotent = true, Mcp = McpExposure.Hidden)]
        public async Task RefreshCachesAsync(CancellationToken cancellationToken = default)
        {
            PersistVerdict.Ensure(accessor.Current, mutation: true, allowWhenReadOnly: false, frames.Open);
            _ = await runner.RunAsync(
                BatchPurpose.Persist,
                (user, batch) =>
                {
                    // --- Every tenant-level tag ---
                    foreach (string name in versionTags.Names)
                    {
                        batch.BumpVersionTag(name);
                    }

                    // --- Both user-level tags of every user, to the batch's tag ---
                    _ = batch.Sql(
                        $"UPDATE [core].[UserStamps] SET [PermissionsTag] = @tm_tag, [PreferencesTag] = @tm_tag;",
                        new SqlOptions(UserStampsWritten, idempotent: true));

                    // --- Every user's preferences moved: announced to the tenant group, without a value ---
                    publisher.Publish(batch, new ClientEvent(VersionTagBumpContributor.CacheChangedEvent, [], new CacheChangedPayload(VersionTagWire.PreferencesItem, null)));
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs a Read batch with nothing but the prologue, which keeps the process's snapshots current.</summary>
        /// <param name="cancellationToken">Cancels the batch.</param>
        /// <returns>A task that completes when the prologue ran.</returns>
        private Task<bool> PrologueAsync(CancellationToken cancellationToken)
        {
            return runner.RunAsync(BatchPurpose.Read, static (_, _) => ValueTask.CompletedTask, static (_, _) => true, cancellationToken);
        }

        /// <summary>
        ///     The categories the caller may read, from one evaluation of <c>Read</c> over the typed
        ///     row's resource and every category's (a <c>Save</c> grant satisfies <c>Read</c>).
        /// </summary>
        /// <param name="cancellationToken">Cancels the evaluation.</param>
        /// <returns>The categories held.</returns>
        private async Task<HeldCategories> HeldAsync(CancellationToken cancellationToken)
        {
            List<string> resources = [CoreResources.SettingsGeneral, .. keys.Categories.Select(static category => category.Resource)];
            SecurableRef[] securables = [.. resources.Distinct(StringComparer.OrdinalIgnoreCase).Select(static resource => new SecurableRef(resource, AccessActions.Read))];
            IReadOnlyList<AccessDecision> decisions = await evaluator.EvaluateAsync(securables, cancellationToken).ConfigureAwait(false);
            HashSet<string> allowed = new(decisions.Where(static decision => decision.IsAllowed).Select(static decision => decision.Resource), StringComparer.OrdinalIgnoreCase);
            return new HeldCategories(
                allowed.Contains(CoreResources.SettingsGeneral),
                [.. keys.Categories.Where(category => allowed.Contains(category.Resource)).Select(static category => category.Name)]);
        }

        /// <summary>
        ///     Appends the editor's reads: the typed row when asked for, every stored entry, and the
        ///     related reads of the reference keys of the held categories, whose ids come from the
        ///     scope's cached view overlaid with what a save writes.
        /// </summary>
        /// <param name="batch">The batch.</param>
        /// <param name="held">The categories held.</param>
        /// <param name="typed">Whether the typed row is read.</param>
        /// <param name="overlay">The stored JSON of the entries a save writes, by key, null for a reset one; null outside a save.</param>
        /// <returns>The reads.</returns>
        /// <exception cref="InvalidOperationException">The scope's context carries no tenant settings.</exception>
        private EditorReads ComposeEditorReads(IDataBatch batch, HeldCategories held, bool typed, IReadOnlyDictionary<string, string?>? overlay)
        {
            TenantSettings settings = accessor.Current.TenantSettings
                ?? throw new InvalidOperationException("The scope's context carries no tenant settings, so the settings editor's related rows cannot be read.");
            BatchResult<RawResult>? row = typed ? SettingsReads.TypedRow(batch) : null;
            BatchResult<RawResult> entries = SettingsReads.Entries(batch, keys: null);

            // --- The reference keys of the held categories, never a secret's, as the cached view holds them ---
            IEnumerable<(SettingKey Key, JsonElement Value)> references = keys.Keys
                .Where(key => key.References is not null && key.Visibility != SettingVisibility.Secret && held.Categories.Contains(SettingKeyRegistry.CategoryOf(key)))
                .Select(key => (key, CachedValue(key, settings, overlay)));
            Action<RelatedEntities> related = _related.Compose(batch, settings.Shape, references);
            return new EditorReads(row, entries, related);
        }

        /// <summary>A key's value as the cached view holds it, overlaid with what a save writes: stored, else the default.</summary>
        /// <param name="key">The key.</param>
        /// <param name="settings">The cached view.</param>
        /// <param name="overlay">What a save writes; null outside a save.</param>
        /// <returns>The value.</returns>
        private static JsonElement CachedValue(SettingKey key, TenantSettings settings, IReadOnlyDictionary<string, string?>? overlay)
        {
            return overlay is not null && overlay.TryGetValue(key.Name, out string? written)
                ? written is not null && TryParse(written, out JsonElement value) ? value : key.DefaultJson
                : settings.Entries.TryGetValue(key.Name, out JsonElement stored) ? stored : key.DefaultJson;
        }

        /// <summary>Parses stored JSON.</summary>
        /// <param name="json">The text.</param>
        /// <param name="value">The value; the default when the text is not JSON.</param>
        /// <returns>False when the text is not JSON.</returns>
        private static bool TryParse(string json, out JsonElement value)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                value = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        ///     The editor's view from the rows read: the typed row when held, each held non-secret
        ///     key's value (stored when it reads as the key's, else the default), and the keys and
        ///     stamps of the held categories' stored rows. Undeclared keys are dropped.
        /// </summary>
        /// <param name="typed">The typed row read; null when it was not read.</param>
        /// <param name="entries">Every stored entry.</param>
        /// <param name="held">The categories held.</param>
        /// <param name="related">The related rows.</param>
        /// <returns>The view.</returns>
        private TenantSettingsDetails BuildDetails(StoredSettings? typed, IReadOnlyDictionary<string, StoredEntry> entries, HeldCategories held, RelatedEntities related)
        {
            Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
            List<string> setKeys = [];
            Dictionary<string, DateTimeOffset> stamps = new(StringComparer.Ordinal);
            foreach (SettingKey key in keys.Keys)
            {
                if (!held.Categories.Contains(SettingKeyRegistry.CategoryOf(key)))
                {
                    continue;
                }

                // --- A stored row: its key and stamp, a secret's included ---
                StoredEntry? row = entries.GetValueOrDefault(key.Name);
                if (row is not null)
                {
                    setKeys.Add(key.Name);
                    stamps[key.Name] = row.ModifiedAt;
                }

                // --- The value, never a secret's ---
                if (key.Visibility != SettingVisibility.Secret)
                {
                    values[key.Name] = row is not null && TryParse(row.Value, out JsonElement stored) && key.TryReadValue(stored, out _, out _)
                        ? stored
                        : key.DefaultJson;
                }
            }

            TypedSettings? general = held.General && typed is not null ? SettingsReads.Entity(typed.Row) : null;
            return new TenantSettingsDetails(
                general,
                values.ToFrozenDictionary(StringComparer.Ordinal),
                setKeys.AsReadOnly(),
                stamps.ToFrozenDictionary(StringComparer.Ordinal),
                related);
        }

        /// <summary>The categories a caller may read.</summary>
        /// <param name="General">Whether the caller may read the typed row.</param>
        /// <param name="Categories">The categories' names, <c>general</c> among them when held.</param>
        private sealed record HeldCategories(bool General, FrozenSet<string> Categories)
        {
            /// <summary>Whether any is held.</summary>
            public bool Any => General || Categories.Count > 0;
        }

        /// <summary>The editor's reads, composed on one batch.</summary>
        /// <param name="Typed">The typed row's read; null when not read.</param>
        /// <param name="Entries">The stored entries' read.</param>
        /// <param name="Related">Fills the related rows.</param>
        private sealed record EditorReads(BatchResult<RawResult>? Typed, BatchResult<RawResult> Entries, Action<RelatedEntities> Related)
        {
            /// <summary>The typed row read; null when not read, or in a database the migrations never seeded.</summary>
            public StoredSettings? TypedRow => Typed is null ? null : SettingsReads.ReadTypedRow(Typed);

            /// <summary>The editor's view of the executed reads.</summary>
            /// <param name="service">The service, which builds the view.</param>
            /// <param name="held">The categories held.</param>
            /// <returns>The view.</returns>
            public TenantSettingsDetails Details(SettingsService service, HeldCategories held)
            {
                return Details(service, held, TypedRow, SettingsReads.ReadEntries(Entries));
            }

            /// <summary>The editor's view of rows already read from the executed reads.</summary>
            /// <param name="service">The service, which builds the view.</param>
            /// <param name="held">The categories held.</param>
            /// <param name="typed">The typed row read.</param>
            /// <param name="entries">The stored entries read.</param>
            /// <returns>The view.</returns>
            public TenantSettingsDetails Details(SettingsService service, HeldCategories held, StoredSettings? typed, IReadOnlyDictionary<string, StoredEntry> entries)
            {
                RelatedEntities related = new();
                Related(related);
                return service.BuildDetails(typed, entries, held, related);
            }
        }
    }
}
