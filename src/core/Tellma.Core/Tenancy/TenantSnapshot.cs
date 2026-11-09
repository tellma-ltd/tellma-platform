// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>Why a catalog row stays out of the snapshot.</summary>
    internal enum TenantRowRefusal
    {
        /// <summary>The stored category or state is not one this runtime knows.</summary>
        UnknownCategoryOrState,

        /// <summary>The server breaks the location grammar or is outside the allowed servers.</summary>
        ServerRefused,

        /// <summary>The database name breaks the location grammar.</summary>
        DatabaseMalformed,

        /// <summary>The row names a profile that is not configured.</summary>
        ProfileUnknown,

        /// <summary>The row's profile is configured but is not a valid connection-string fragment.</summary>
        ProfileMalformed,
    }

    /// <summary>A catalog row the snapshot refused, and why.</summary>
    /// <param name="Row">The row.</param>
    /// <param name="Reason">Why it was refused.</param>
    internal sealed record RefusedTenantRow(TenantRow Row, TenantRowRefusal Reason);

    /// <summary>
    ///     Turns catalog rows into tenants: validates each row's location against the location
    ///     grammar and the allowed servers, and composes its connection string from its profile.
    ///     Each profile is parsed once per composer.
    /// </summary>
    /// <param name="sql">The SQL options in effect.</param>
    /// <param name="isDevelopment">Whether the host runs in Development.</param>
    /// <param name="applicationName">The deployment id, every connection's application name.</param>
    internal sealed class TenantRowComposer(TellmaSqlOptions sql, bool isDevelopment, string applicationName)
    {
        private readonly Dictionary<string, SqlConnectionStringBuilder?> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly string[] _allowedServers = [.. sql.AllowedServers];

        /// <summary>Whether a profile keeps warm connections, a non-zero <c>Min Pool Size</c>.</summary>
        /// <param name="profileName">The profile name.</param>
        /// <returns>True when it does.</returns>
        public bool IsWarm(string profileName)
        {
            return Profile(profileName) is { MinPoolSize: > 0 };
        }

        /// <summary>Composes a row into a tenant, or says why it cannot be served.</summary>
        /// <param name="row">The row.</param>
        /// <param name="tenant">The tenant, when the row is served.</param>
        /// <param name="refusal">Why the row is refused, when it is.</param>
        /// <returns>True when the row is served.</returns>
        public bool TryCompose(TenantRow row, out TenantInfo tenant, out TenantRowRefusal refusal)
        {
            ArgumentNullException.ThrowIfNull(row);
            tenant = null!;

            // --- The first rule the row breaks, in order: what it is, where it points, how it connects ---
            TenantDescriptor? descriptor = row.ToDescriptor();
            TenantRowRefusal? reason = descriptor is null ? TenantRowRefusal.UnknownCategoryOrState
                : !TenantLocations.IsServerAllowed(row.Server, _allowedServers, isDevelopment) ? TenantRowRefusal.ServerRefused
                : !TenantLocations.IsValidDatabaseName(row.Database) ? TenantRowRefusal.DatabaseMalformed
                : !sql.Profiles.ContainsKey(row.CredentialProfile) ? TenantRowRefusal.ProfileUnknown
                : Profile(row.CredentialProfile) is null ? TenantRowRefusal.ProfileMalformed
                : null;

            if (descriptor is null || reason is not null)
            {
                refusal = reason ?? TenantRowRefusal.UnknownCategoryOrState;
                return false;
            }

            // --- A served row: its string, composed once, here ---
            refusal = default;
            string connectionString = TenantConnectionStrings.Compose(Profile(row.CredentialProfile)!, row.Server, row.Database, applicationName);
            tenant = new TenantInfo(
                descriptor,
                new TenantLocation(row.Server, row.Database, row.CredentialProfile),
                connectionString,
                row.Properties,
                row.Version);
            return true;
        }

        /// <summary>The parsed fragment of a configured profile, or null when it is absent or does not parse.</summary>
        /// <param name="profileName">The profile name.</param>
        /// <returns>The parsed fragment, or null.</returns>
        private SqlConnectionStringBuilder? Profile(string profileName)
        {
            if (!_profiles.TryGetValue(profileName, out SqlConnectionStringBuilder? parsed))
            {
                parsed = sql.Profiles.TryGetValue(profileName, out string? fragment) && TenantConnectionStrings.TryParse(fragment, out SqlConnectionStringBuilder builder)
                    ? builder
                    : null;
                _profiles[profileName] = parsed;
            }

            return parsed;
        }
    }

    /// <summary>
    ///     One immutable snapshot of the catalog's tenants: the tenants the registry serves, by id, the
    ///     rows it refused, and the catalog version it was loaded from.
    /// </summary>
    internal sealed class TenantSnapshot
    {
        private readonly FrozenDictionary<int, TenantInfo> _byId;

        /// <summary>Creates a snapshot.</summary>
        /// <param name="version">The catalog version the rows were read at.</param>
        /// <param name="tenants">The served tenants.</param>
        /// <param name="refused">The refused rows.</param>
        /// <param name="warmTenants">How many served tenants use a profile that keeps warm connections.</param>
        private TenantSnapshot(Guid version, IReadOnlyList<TenantInfo> tenants, IReadOnlyList<RefusedTenantRow> refused, int warmTenants)
        {
            Version = version;
            Tenants = tenants;
            Refused = refused;
            WarmTenants = warmTenants;
            _byId = tenants.ToFrozenDictionary(static tenant => tenant.Id);
        }

        /// <summary>The <c>catalog.CatalogState</c> version the rows were read at.</summary>
        public Guid Version { get; }

        /// <summary>The served tenants, ordered by id.</summary>
        public IReadOnlyList<TenantInfo> Tenants { get; }

        /// <summary>The rows left out, ordered by id.</summary>
        public IReadOnlyList<RefusedTenantRow> Refused { get; }

        /// <summary>How many served tenants use a profile with a non-zero <c>Min Pool Size</c>.</summary>
        public int WarmTenants { get; }

        /// <summary>Builds a snapshot from the rows of one reload.</summary>
        /// <param name="version">The catalog version the rows were read at.</param>
        /// <param name="rows">The rows.</param>
        /// <param name="composer">Composes each row.</param>
        /// <returns>The snapshot.</returns>
        public static TenantSnapshot Build(Guid version, IReadOnlyList<TenantRow> rows, TenantRowComposer composer)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(composer);

            List<TenantInfo> tenants = [];
            List<RefusedTenantRow> refused = [];
            int warm = 0;
            foreach (TenantRow row in rows.OrderBy(static row => row.Id))
            {
                if (composer.TryCompose(row, out TenantInfo tenant, out TenantRowRefusal refusal))
                {
                    tenants.Add(tenant);
                    warm += composer.IsWarm(row.CredentialProfile) ? 1 : 0;
                }
                else
                {
                    refused.Add(new RefusedTenantRow(row, refusal));
                }
            }

            return new TenantSnapshot(version, tenants.AsReadOnly(), refused.AsReadOnly(), warm);
        }

        /// <summary>The served tenant of an id.</summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <returns>The tenant, or null.</returns>
        public TenantInfo? Find(int tenantId)
        {
            return _byId.GetValueOrDefault(tenantId);
        }
    }
}
