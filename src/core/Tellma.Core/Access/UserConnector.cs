// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;
using Tellma.Core.Settings;

namespace Tellma.Core.Access
{
    // The connector is spec 0014 §7.1; the caches it keeps are §6.2 and §6.3, the premises it builds
    // follow §7.2's rule for the expected tags and §7.8's for the stamp and the flip.
    /// <summary>
    ///     Connects the scope's principal to its user in the tenant, through the connect prologue,
    ///     and keeps the process's connect, permission and profile caches from what each prologue
    ///     read. One per scope; the scope's connected user is the last one it applied for the scope.
    /// </summary>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="database">The scope's tenant database handle, on which the connector's own round trips run.</param>
    /// <param name="services">The connector's collaborators: the caches and the builder.</param>
    /// <param name="settings">The scope's tenant settings, which a set built before the context carries them is validated under.</param>
    /// <param name="contributors">The scope's batch contributors, among them the settings cold load the prologue absorbs.</param>
    /// <param name="options">The access options: the permission sets' maximum age and the activity stamp's throttle.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    internal sealed class UserConnector(
        IRequestContextAccessor accessor,
        ITenantDatabase database,
        ConnectorServices services,
        ITenantSettingsCache settings,
        IEnumerable<IDataBatchContributor> contributors,
        IOptions<AccessOptions> options,
        ILoggerFactory loggerFactory) : IUserConnector, IScopeConnection
    {
        /// <summary>The system user's profile: the seeded name, nothing else.</summary>
        private static readonly UserProfile SystemProfile = new(
            AccessSeeds.SystemUserName, null, null, null, null, null, null, null, null, null, null, null, null, null, AccessSeeds.SeededAt);

        /// <summary>Where each result this connector's prologues read came from.</summary>
        private readonly ConditionalWeakTable<ConnectResult, Origin> _origins = [];

        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);

        /// <summary>How the scope connected; none until it did.</summary>
        private ConnectionMode _mode;

        /// <summary>How a scope connected, which selects the premises of its batches.</summary>
        private enum ConnectionMode
        {
            /// <summary>Not connected yet.</summary>
            None,

            /// <summary>A request's caller, by subject.</summary>
            Subject,

            /// <summary>A background scope running as a user, by id.</summary>
            User,

            /// <summary>The platform's own work.</summary>
            System,
        }

        /// <summary>The scope's connected user; null until the scope connected.</summary>
        public ConnectedUser? Current { get; private set; }

        /// <inheritdoc />
        public RequestContextInputs Inputs { get; set; } = RequestContextInputs.Empty;

        /// <summary>The access options.</summary>
        private AccessOptions Options => options.Value;

        /// <summary>The settings cold load, which the prologue absorbs; resolved on first use.</summary>
        private SettingsColdLoadContributor ColdLoad => field ??= contributors.OfType<SettingsColdLoadContributor>().FirstOrDefault()
            ?? throw new InvalidOperationException("The settings cold load is missing from the composition; the connect prologue absorbs it.");

        /// <inheritdoc />
        public async Task<ConnectedUser> ConnectAsync(CancellationToken cancellationToken = default)
        {
            RequestContext context = accessor.Current;
            EnsureCaller(context);
            TenantDescriptor tenant = context.RequireTenant();
            string subject = context.Subject ?? throw new InvalidOperationException("The scope's caller has no subject to connect by.");

            // --- A subject the column could never hold matches no user; it is refused without a round trip ---
            if (!IsBindable(subject))
            {
                services.Metrics.CountConnect(AccessTelemetryNames.ResultUnknownSubject);
                AccessLog.ConnectRejected(_logger, tenant.Id, null, AccessTelemetryNames.ResultUnknownSubject);
                throw new TenantNotFoundException(tenant.Id);
            }

            // --- The cached connection; every batch's prologue re-validates it ---
            if (services.Connections.Find(tenant.Id, subject) is ConnectedUser cached)
            {
                Connected(ConnectionMode.Subject, cached);
                return cached;
            }

            // --- Otherwise one prologue-only round trip, shared by the subject's concurrent first requests ---
            ConnectedUser user = await services.Connections.ConnectOnceAsync(
                tenant.Id,
                subject,
                token => RoundTripAsync(ConnectPremises.Cold(subject, ExpectedSettingsTag(context), StampAllowed(context, null), FlipAllowed(context)), token),
                cancellationToken).ConfigureAwait(false);
            Connected(ConnectionMode.Subject, user);
            return user;
        }

        /// <inheritdoc />
        public async Task<ConnectedUser> ConnectAsUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            RequestContext context = accessor.Current;
            EnsureCaller(context);
            int tenantId = context.RequireTenant().Id;

            // --- The cached set and profile the snapshots hold current are expected; anything else reads stale ---
            TenantUserCacheKey key = new(tenantId, userId);
            Guid permissionsTag = services.Snapshots.Current(tenantId)[VersionTagNames.Permissions].Value;
            UserVersionTagSnapshot? stamp = services.UserSnapshots.Current(tenantId, userId);
            UserAccess? access = stamp is null ? null : services.Permissions.Peek(key, set => set.Tag == permissionsTag && set.UserTag == stamp.Permissions.Value);
            bool profiled = stamp is not null && services.Profiles.Peek(key, stamp.Preferences) is not null;
            ConnectExpectations expected = new(
                access?.Tag,
                access?.UserTag,
                profiled ? stamp!.Preferences.Value : null,
                ExpectedSettingsTag(context));

            ConnectedUser user = await RoundTripAsync(new ConnectPremises.ForUser(userId, expected, access is not null && IsAged(access)), cancellationToken).ConfigureAwait(false);
            Connected(ConnectionMode.User, user);
            return user;
        }

        /// <inheritdoc />
        public async Task<ConnectedUser> ConnectAsSystemAsync(CancellationToken cancellationToken = default)
        {
            RequestContext context = accessor.Current;
            if (!context.IsSystem)
            {
                throw new InvalidOperationException("Only the platform's own work connects as the system user; the scope runs for another principal.");
            }

            // --- Settings the store holds current need no round trip: the system's set is never loaded ---
            int tenantId = context.RequireTenant().Id;
            Guid expected = ExpectedSettingsTag(context);
            ConnectedUser user = expected != Guid.Empty
                ? SystemUser(services.Snapshots.Current(tenantId).Tags.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value, StringComparer.Ordinal))
                : await RoundTripAsync(new ConnectPremises.ForSystem(expected), cancellationToken).ConfigureAwait(false);
            Connected(ConnectionMode.System, user);
            return user;
        }

        /// <inheritdoc />
        public BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(premises);
            if (batch is not DataBatch data)
            {
                throw new ArgumentException("The batch is not one of the platform's tenant batches.", nameof(batch));
            }

            RequestContext context = accessor.Current;
            int tenantId = context.RequireTenant().Id;
            if (data.Connection.TenantId != tenantId)
            {
                throw new ArgumentException("The batch is not one of the scope's tenant database.", nameof(batch));
            }

            // --- The caller's own tags go into the batch's outcome; another user's (a load of their permissions) never do ---
            bool reportUserTags = premises is ConnectPremises.ForSubject
                || (premises is ConnectPremises.ForUser forUser && (context.UserId is null || context.UserId == forUser.UserId));
            ConnectReading reading = new(
                services.Time,
                reportUserTags,
                duration => services.Metrics.RecordPrologue(premises.Variant, duration),
                result => _origins.AddOrUpdate(result, new Origin(tenantId, premises)));
            return ConnectPrologue.Append(data, premises, ColdLoad, reading);
        }

        /// <inheritdoc />
        public ConnectedUser Apply(ConnectResult result)
        {
            return Apply(result, Current);
        }

        /// <inheritdoc />
        ConnectedUser IScopeConnection.Apply(ConnectResult result, ConnectedUser basis)
        {
            return Apply(result, basis);
        }

        /// <inheritdoc />
        public async Task<ConnectedUser> CurrentAsync(CancellationToken cancellationToken)
        {
            if (Current is ConnectedUser current)
            {
                return current;
            }

            RequestContext context = accessor.Current;
            return context.Kind switch
            {
                PrincipalKind.System => await ConnectAsSystemAsync(cancellationToken).ConfigureAwait(false),
                PrincipalKind.User or PrincipalKind.ServiceAccount when context.UserId is int userId && _mode == ConnectionMode.User
                    => await ConnectAsUserAsync(userId, cancellationToken).ConfigureAwait(false),
                PrincipalKind.User or PrincipalKind.ServiceAccount when context.Subject is not null
                    => await ConnectAsync(cancellationToken).ConfigureAwait(false),
                PrincipalKind.User or PrincipalKind.ServiceAccount when context.UserId is int userId
                    => await ConnectAsUserAsync(userId, cancellationToken).ConfigureAwait(false),
                PrincipalKind.Anonymous or PrincipalKind.User or PrincipalKind.ServiceAccount => throw new TenantNotFoundException(context.RequireTenant().Id),
                _ => throw new InvalidOperationException($"The principal kind {context.Kind} is unknown to the user connection."),
            };
        }

        /// <inheritdoc />
        public ConnectPremises PremisesFor(ConnectedUser user)
        {
            ArgumentNullException.ThrowIfNull(user);
            RequestContext context = accessor.Current;
            Guid settingsTag = ExpectedSettingsTag(context);
            return _mode switch
            {
                ConnectionMode.System => new ConnectPremises.ForSystem(settingsTag),
                ConnectionMode.User => new ConnectPremises.ForUser(
                    user.UserId, new ConnectExpectations(user.Access.Tag, user.Access.UserTag, user.PreferencesTag, settingsTag), IsAged(user.Access)),
                ConnectionMode.Subject => ConnectPremises.For(
                    user,
                    context.Subject ?? throw new InvalidOperationException("The scope's caller has no subject to connect by."),
                    settingsTag,
                    StampAllowed(context, user),
                    FlipAllowed(context),
                    IsAged(user.Access)),
                ConnectionMode.None => throw new InvalidOperationException("The scope is not connected; connect it before composing a batch for it."),
                _ => throw new InvalidOperationException($"The connection mode {_mode} is unknown."),
            };
        }

        /// <inheritdoc />
        public async Task<ConnectedUser> ReconnectColdAsync(CancellationToken cancellationToken)
        {
            RequestContext context = accessor.Current;
            Guid settingsTag = ExpectedSettingsTag(context);
            ConnectPremises premises = _mode switch
            {
                ConnectionMode.System => new ConnectPremises.ForSystem(settingsTag),
                ConnectionMode.User => new ConnectPremises.ForUser(
                    Current?.UserId ?? context.UserId ?? throw new InvalidOperationException("The background scope names no user."),
                    new ConnectExpectations(null, null, null, settingsTag),
                    ReloadPermissions: false),
                ConnectionMode.Subject => ConnectPremises.Cold(
                    context.Subject ?? throw new InvalidOperationException("The scope's caller has no subject to connect by."),
                    settingsTag,
                    stampActivity: false,
                    allowStateFlip: false),
                ConnectionMode.None => throw new InvalidOperationException("The scope is not connected; connect it before re-connecting it."),
                _ => throw new InvalidOperationException($"The connection mode {_mode} is unknown."),
            };

            return await RoundTripAsync(premises, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Drop(ConnectedUser user)
        {
            ArgumentNullException.ThrowIfNull(user);
            RequestContext context = accessor.Current;
            int tenantId = context.RequireTenant().Id;
            if (_mode == ConnectionMode.Subject && context.Subject is string subject)
            {
                services.Connections.Remove(tenantId, subject);
            }

            TenantUserCacheKey key = new(tenantId, user.UserId);
            services.Permissions.Remove(key);
            services.Profiles.Remove(key);
        }

        /// <summary>Whether a subject can be bound as the column's type: 1 to 255 printable ASCII characters.</summary>
        /// <param name="subject">The subject.</param>
        /// <returns>True when it can.</returns>
        internal static bool IsBindable(string subject)
        {
            // varchar(255) under a binary collation: a character outside printable ASCII would be
            // converted through the code page, and could match another subject's bytes.
            return subject.Length is > 0 and <= 255 && subject.All(static c => c is >= '!' and <= '~');
        }

        /// <summary>Refuses a scope whose principal is not a user or a service account.</summary>
        /// <param name="context">The scope's context.</param>
        /// <exception cref="InvalidOperationException">The scope is the platform's own, or anonymous.</exception>
        private static void EnsureCaller(RequestContext context)
        {
            if (context.Kind is not (PrincipalKind.User or PrincipalKind.ServiceAccount))
            {
                throw new InvalidOperationException($"Only a user's or a service account's scope connects a caller; the scope's principal is {context.Kind}.");
            }
        }

        /// <summary>Whether the prologue may record the caller's first request: never on a frozen tenant.</summary>
        /// <param name="context">The scope's context.</param>
        /// <returns>True when it may.</returns>
        private static bool FlipAllowed(RequestContext context)
        {
            return context.Tenant?.State != TenantState.Frozen;
        }

        /// <summary>Records how the scope connected, and as whom.</summary>
        /// <param name="mode">How.</param>
        /// <param name="user">The connected user.</param>
        private void Connected(ConnectionMode mode, ConnectedUser user)
        {
            _mode = mode;
            Current = user;
        }

        /// <summary>
        ///     Whether the prologue may stamp the caller's activity: not on a frozen tenant, not for an
        ///     endpoint or a request that asked for none, and not within the throttle since the last
        ///     stamp this instance made.
        /// </summary>
        /// <param name="context">The scope's context.</param>
        /// <param name="user">The connected user, when one is cached.</param>
        /// <returns>True when it may.</returns>
        private bool StampAllowed(RequestContext context, ConnectedUser? user)
        {
            return context.Tenant?.State != TenantState.Frozen
                && !Inputs.SuppressActivityStamp
                && (user?.LastActivityStampedAt is not DateTimeOffset stamped || services.Time.GetUtcNow() - stamped >= Options.ActivityStampInterval);
        }

        /// <summary>Whether a set is older than the maximum age, so the next prologue reloads its rows.</summary>
        /// <param name="access">The set.</param>
        /// <returns>True when it is.</returns>
        private bool IsAged(UserAccess access)
        {
            return !access.IsSystem && services.Time.GetUtcNow() - access.ComputedAt > Options.PermissionsMaxAge;
        }

        /// <summary>
        ///     The tag of the settings a batch composes under: the context's, else the settings
        ///     store's current entry's, else none.
        /// </summary>
        /// <param name="context">The scope's context.</param>
        /// <returns>The tag; <see cref="Guid.Empty" /> for none.</returns>
        private Guid ExpectedSettingsTag(RequestContext context)
        {
            if (context.TenantSettings is TenantSettings bound)
            {
                return bound.Tag.Value;
            }

            int tenantId = context.RequireTenant().Id;
            VersionTag current = services.Snapshots.Current(tenantId)[VersionTagNames.Settings];
            return services.SettingsStore.IsCurrent(tenantId, current) ? current.Value : Guid.Empty;
        }

        /// <summary>One prologue-only round trip for the premises, applied.</summary>
        /// <param name="premises">The premises.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The connected user.</returns>
        private async Task<ConnectedUser> RoundTripAsync(ConnectPremises premises, CancellationToken cancellationToken)
        {
            IDataBatch batch = database.CreateBatch(BatchPurpose.Read);
            BatchResult<ConnectResult> result = Contribute(batch, premises);
            _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return Apply(result.Value, Current);
        }

        /// <summary>Applies a result over the connected user its batch was composed for.</summary>
        /// <param name="result">The result.</param>
        /// <param name="basis">The connected user the batch was composed for; null for none.</param>
        /// <returns>The connected user the result describes.</returns>
        /// <exception cref="ArgumentException">The result is not one of this connector's.</exception>
        /// <exception cref="TenantNotFoundException">The result names no user, or an inactive one.</exception>
        private ConnectedUser Apply(ConnectResult result, ConnectedUser? basis)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (!_origins.TryGetValue(result, out Origin? origin))
            {
                throw new ArgumentException("The result is not one this connector's prologue read.", nameof(result));
            }

            int tenantId = origin.TenantId;
            RequestContext context = accessor.Current;

            // --- The platform's own: the system set, never loaded ---
            if (origin.Premises is ConnectPremises.ForSystem)
            {
                services.Metrics.CountConnect(result.SettingsStale ? AccessTelemetryNames.ResultStaleSettings : AccessTelemetryNames.ResultWarm);
                ConnectedUser system = SystemUser(result.TenantTags);
                if (context.IsSystem)
                {
                    Connected(ConnectionMode.System, system);
                }

                return system;
            }

            // --- A refusal drops what the instance holds of the caller ---
            string? subject = (origin.Premises as ConnectPremises.ForSubject)?.Subject;
            if (result.UserId is not int userId)
            {
                if (subject is not null)
                {
                    services.Connections.Remove(tenantId, subject);
                }

                throw Refused(tenantId, null, AccessTelemetryNames.ResultUnknownSubject);
            }

            TenantUserCacheKey key = new(tenantId, userId);
            if (!result.IsActive || result.Kind is not UserKind kind)
            {
                if (subject is not null)
                {
                    services.Connections.Remove(tenantId, subject);
                }

                services.Permissions.Remove(key);
                services.Profiles.Remove(key);
                throw Refused(tenantId, userId, AccessTelemetryNames.ResultDeactivated);
            }

            // A basis of another tenant or user confirms nothing.
            ConnectedUser? own = basis is not null && basis.UserId == userId && (basis.Access.IsSystem || basis.Access.TenantId == tenantId) ? basis : null;
            DateTimeOffset now = services.Time.GetUtcNow();

            // --- The permission set: rebuilt from fresh rows, else the one whose tags the prologue confirmed ---
            Guid tenantTag = result.TenantTags.TryGetValue(VersionTagNames.Permissions, out Guid permissions) ? permissions : Guid.Empty;
            Guid userTag = result.UserPermissionsTag ?? Guid.Empty;
            UserAccess access;
            if (kind == UserKind.System)
            {
                access = UserAccess.System;
            }
            else if (result.PermissionRows is IReadOnlyList<PermissionRow> rows)
            {
                TenantSettings schemaSettings = SettingsFor(context, result);
                access = services.Builder.Build(tenantId, userId, tenantTag, userTag, rows, services.Schemas.GetSchema(schemaSettings.Shape));

                // Rows that came only because the set was older than its maximum age replace it as aged.
                services.Permissions.Set(key, access, VersionTag.None, aged: !result.PermissionsStale);
            }
            else
            {
                access = (own?.Access is UserAccess held && held.Tag == tenantTag && held.UserTag == userTag ? held : null)
                    ?? services.Permissions.Peek(key, set => set.Tag == tenantTag && set.UserTag == userTag)
                    ?? throw new InvalidOperationException("The connect prologue confirmed a permission set this instance no longer holds.");
            }

            // A prologue that read the set's own tags validates it.
            if (!result.PermissionsStale)
            {
                access.MarkValidated(now);
            }

            // --- The profile: the fresh one, else the one whose tag the prologue confirmed ---
            Guid preferencesTag = result.PreferencesTag ?? Guid.Empty;
            UserProfile profile;
            if (result.Profile is UserProfile fresh)
            {
                profile = fresh;
                services.Profiles.Set(key, fresh, new VersionTag(preferencesTag));
            }
            else
            {
                profile = (own is not null && own.PreferencesTag == preferencesTag ? own.Profile : null)
                    ?? services.Profiles.Peek(key, new VersionTag(preferencesTag))
                    ?? throw new InvalidOperationException("The connect prologue confirmed a profile this instance no longer holds.");
            }

            // --- The throttle memo: a prologue that stamped leaves the stamp within the interval ---
            DateTimeOffset? stamped = origin.Premises is ConnectPremises.ForSubject { StampActivity: true } ? now : own?.LastActivityStampedAt;
            ConnectedUser user = new(userId, kind, result.State ?? UserState.Joined, profile, preferencesTag, access, result.TenantTags, stamped);

            // --- The connect cache, and the scope's user when the result is the scope's caller ---
            if (subject is not null)
            {
                services.Connections.Set(tenantId, subject, user);
                if (context.UserId is int bound && bound != userId)
                {
                    // The subject now names another user than the one the scope is connected as.
                    throw Refused(tenantId, bound, AccessTelemetryNames.ResultUnknownSubject);
                }

                Connected(ConnectionMode.Subject, user);
            }
            else if (context.UserId is null || context.UserId == userId)
            {
                Connected(ConnectionMode.User, user);
            }

            CountResult(origin.Premises, result);
            return user;
        }

        /// <summary>The settings a fresh permission set's query schema is built under.</summary>
        /// <param name="context">The scope's context.</param>
        /// <param name="result">The prologue's result.</param>
        /// <returns>The settings.</returns>
        /// <exception cref="InvalidOperationException">No settings are cached.</exception>
        private TenantSettings SettingsFor(RequestContext context, ConnectResult result)
        {
            // The context's when they are the tenant's current ones; else the store's, which the
            // prologue's absorbed load filled when they were stale or missing.
            Guid current = result.TenantTags.TryGetValue(VersionTagNames.Settings, out Guid tag) ? tag : Guid.Empty;
            return context.TenantSettings is TenantSettings bound && bound.Tag.Value == current
                ? bound
                : settings.Peek() ?? context.TenantSettings
                    ?? throw new InvalidOperationException("No tenant settings are cached to build the permission set's query schema under.");
        }

        /// <summary>Counts a connect result by its most consequential part.</summary>
        /// <param name="premises">The premises the prologue ran under.</param>
        /// <param name="result">The result.</param>
        private void CountResult(ConnectPremises premises, ConnectResult result)
        {
            ConnectExpectations? expected = premises switch
            {
                ConnectPremises.ForSubject subject => subject.Expected,
                ConnectPremises.ForUser user => user.Expected,
                _ => null,
            };

            bool cold = expected is { PermissionsTag: null, UserPermissionsTag: null, PreferencesTag: null };
            services.Metrics.CountConnect(
                cold ? AccessTelemetryNames.ResultCold
                : result.PermissionsStale ? AccessTelemetryNames.ResultStalePermissions
                : result.SettingsStale ? AccessTelemetryNames.ResultStaleSettings
                : result.ProfileStale ? AccessTelemetryNames.ResultStaleProfile
                : AccessTelemetryNames.ResultWarm);
        }

        /// <summary>Counts and logs a refused connection, and returns its exception.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user the prologue matched, if any.</param>
        /// <param name="reason">The result's tag value.</param>
        /// <returns>The exception.</returns>
        private TenantNotFoundException Refused(int tenantId, int? userId, string reason)
        {
            services.Metrics.CountConnect(reason);
            AccessLog.ConnectRejected(_logger, tenantId, userId, reason);
            return new TenantNotFoundException(tenantId);
        }

        /// <summary>The system user, connected with the tenant's tags.</summary>
        /// <param name="tags">The tenant's tags.</param>
        /// <returns>The connected system user.</returns>
        private static ConnectedUser SystemUser(IReadOnlyDictionary<string, Guid> tags)
        {
            return new ConnectedUser(WellKnownIds.SystemUserId, UserKind.System, UserState.Joined, SystemProfile, Guid.Empty, UserAccess.System, tags, null);
        }

        /// <summary>Where a result came from.</summary>
        /// <param name="TenantId">The tenant whose batch read it.</param>
        /// <param name="Premises">The premises the prologue ran under.</param>
        private sealed record Origin(int TenantId, ConnectPremises Premises);
    }

    /// <summary>The process-wide collaborators of the user connector, gathered so its scope adds few dependencies.</summary>
    /// <param name="Connections">The connect cache.</param>
    /// <param name="Permissions">The permission sets.</param>
    /// <param name="Profiles">The profiles.</param>
    /// <param name="Builder">Builds a set from its rows.</param>
    /// <param name="Schemas">The query schemas a set's filters are validated under.</param>
    /// <param name="SettingsStore">The settings store, whose current entry's tag the premises expect before the context carries settings.</param>
    /// <param name="Snapshots">The process's tenant tag snapshots.</param>
    /// <param name="UserSnapshots">The process's user tag snapshots.</param>
    /// <param name="Metrics">The access instruments.</param>
    /// <param name="Time">The clock.</param>
    internal sealed record ConnectorServices(
        ConnectCache Connections,
        PermissionsCache Permissions,
        ProfileCache Profiles,
        UserAccessBuilder Builder,
        IQueryexSchemaProvider Schemas,
        TenantSettingsStore SettingsStore,
        IVersionTagSnapshots Snapshots,
        IUserVersionTagSnapshots UserSnapshots,
        AccessMetrics Metrics,
        TimeProvider Time);
}
