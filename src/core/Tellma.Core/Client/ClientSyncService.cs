// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Users;

namespace Tellma.Core.Client
{
    // The service is spec 0025 §5.1. One Read batch carries the connect prologue (spec 0014 §7.3),
    // which loads the permission rows and the profile only when stale and the settings only when
    // stale or cold (spec 0013 §5.7), then the caller's preference rows, read unconditionally since
    // whether the envelope is needed is known only after the prologue. Everything else is in memory
    // after that round trip: the settings bytes the cache serialized when it loaded them, the lists'
    // tags from the snapshot, the access summary over the set the round trip validated, and the
    // tenant's facts. The route is a polling route, so it never stamps the caller's activity.

    /// <summary>
    ///     The one call a browser tab revalidates a tenant's cached documents with: it compares the
    ///     tags the tab holds with the current ones and answers only what changed.
    /// </summary>
    /// <param name="runner">Runs the batch behind the connect prologue.</param>
    /// <param name="accessor">The scope's request context, which names the tenant.</param>
    /// <param name="settings">The scope's view of the tenant's settings, which holds their client bytes.</param>
    /// <param name="cacheable">The scope's view of the cacheable entities' lists.</param>
    /// <param name="realtime">Whether this host serves the real-time hub.</param>
    /// <param name="securables">Every registered securable, which the access summary decides.</param>
    /// <param name="evaluator">Decides each securable over the caller's set.</param>
    /// <param name="time">The clock the access summary is valid from.</param>
    [ApiRoute(Route)]
    internal sealed class ClientSyncService(
        IGuardedBatchRunner runner,
        IRequestContextAccessor accessor,
        ITenantSettingsCache settings,
        ICacheableEntities cacheable,
        IRealtimeStatus realtime,
        ISecurableRegistry securables,
        AccessEvaluator evaluator,
        TimeProvider time)
    {
        /// <summary>The service's route segment.</summary>
        public const string Route = "client";

        /// <summary>The name of the sync action.</summary>
        public const string SyncAction = "sync";

        /// <summary>
        ///     Revalidates every cached document of the tenant in one round trip: the tenant's facts and
        ///     current tags always, the settings when the held <c>settings</c> tag differs, the caller's
        ///     envelope when either caller tag differs, every list's tag when the held
        ///     <c>entities</c> tag differs, and the held lists whose tags moved. Not offered to agents,
        ///     which hold no client cache.
        /// </summary>
        /// <param name="request">The tags the tab holds; empty for a cold call.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>What changed.</returns>
        [ApiAction(SyncAction, MemberOnly = true, Idempotent = true, Mutation = false, Mcp = McpExposure.Hidden)]
        public async Task<ClientSyncResult> SyncAsync(ClientSyncRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            IReadOnlyDictionary<string, string> held = request.Held;

            // --- One Read batch: the connect prologue, then the caller's preference rows ---
            BatchResult<RawResult>? bag = null;
            (ConnectedUser caller, BatchOutcome outcome) = await runner.RunAsync(
                BatchPurpose.Read,
                (_, batch) =>
                {
                    bag = MeEnvelope.ComposeBag(batch, idempotent: true);
                    return ValueTask.CompletedTask;
                },
                static (user, outcome) => (user, outcome),
                cancellationToken).ConfigureAwait(false);

            // --- The four client tags as of that round trip, as the version-tags header carries them ---
            UserVersionTagSnapshot callerTags = MeEnvelope.CallerTags(outcome.UserVersionTags);
            Dictionary<string, string> tags = new(StringComparer.Ordinal);
            foreach ((string name, string value) in VersionTagWire.Items(outcome.VersionTags, callerTags))
            {
                tags[name] = value;
            }

            // --- The settings, as the cache's bytes under the tag they were loaded with ---
            PreSerializedJson<TenantSettingsForClient>? client = null;
            if (!Holds(held, VersionTagWire.SettingsItem, tags[VersionTagWire.SettingsItem]))
            {
                (byte[] json, string wire) = await settings.GetForClientAsync().ConfigureAwait(false);
                client = new PreSerializedJson<TenantSettingsForClient>(json);
                tags[VersionTagWire.SettingsItem] = wire;
            }

            // --- The envelope, composed exactly as me composes it, and serialized as me writes it ---
            PreSerializedJson<MeResult>? me = null;
            if (!Holds(held, VersionTagWire.PreferencesItem, tags[VersionTagWire.PreferencesItem])
                || !Holds(held, VersionTagWire.PermissionsItem, tags[VersionTagWire.PermissionsItem]))
            {
                me = PreSerializedJson<MeResult>.Of(MeEnvelope.Of(
                    MeEnvelope.View(caller),
                    MeEnvelope.Bag(bag!.Value),
                    outcome.VersionTags,
                    callerTags,
                    caller.Access,
                    securables,
                    evaluator,
                    time.GetUtcNow()));
            }

            // --- The lists: every list's tag when the composite moved, and the held lists that moved ---
            IReadOnlyDictionary<string, string> lists = await cacheable.GetWireTagsAsync().ConfigureAwait(false);
            bool everyList = !Holds(held, VersionTagWire.EntitiesItem, tags[VersionTagWire.EntitiesItem]);
            List<string> stale = [];
            foreach ((string entity, string wire) in lists.OrderBy(static list => list.Key, StringComparer.Ordinal))
            {
                string name = VersionTagNames.ForEntity(entity);
                if (everyList)
                {
                    tags[name] = wire;
                }

                if (held.TryGetValue(name, out string? mine) && !string.Equals(mine, wire, StringComparison.Ordinal))
                {
                    stale.Add(entity);
                }
            }

            // --- The tenant's facts ---
            TenantDescriptor tenant = accessor.Current.RequireTenant();
            TenantFacts facts = new(tenant.Id, tenant.Category, tenant.State, tenant.LiveTenantId, realtime.Enabled);
            return new ClientSyncResult(facts, tags, client, me, stale);
        }

        /// <summary>Whether the tab holds a tag's current wire form; ordinal, and an absent tag is not held.</summary>
        /// <param name="held">The tags the tab holds.</param>
        /// <param name="name">The tag's name.</param>
        /// <param name="current">The tag's current wire form.</param>
        /// <returns>True when the held tag equals the current one.</returns>
        private static bool Holds(IReadOnlyDictionary<string, string> held, string name, string current)
        {
            return held.TryGetValue(name, out string? mine) && string.Equals(mine, current, StringComparison.Ordinal);
        }
    }
}
