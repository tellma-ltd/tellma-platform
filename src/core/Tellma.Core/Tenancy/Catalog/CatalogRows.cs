// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy.Catalog
{
    /// <summary>
    ///     A row of <c>catalog.Tenants</c>: one tenant's registration, state and database location.
    ///     Mapped for migrations only; the catalog's statements read and write the table directly.
    /// </summary>
    /// <remarks>
    ///     The row holds a location and a credential profile's name, never a connection string, a
    ///     password, a key or a token; a model test pins the column set.
    /// </remarks>
    internal sealed class CatalogTenant
    {
        /// <summary>The tenant id: from <c>catalog.sq_Tenants</c>, or a requested id in 1–999.</summary>
        public int Id { get; set; }

        /// <summary>The display name in the first content language.</summary>
        public string Name { get; set; } = null!;

        /// <summary>The display name in the second content language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The display name in the third content language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The first content-language code.</summary>
        public string Language1 { get; set; } = null!;

        /// <summary>The second content-language code, paired with <see cref="Name2" />.</summary>
        public string? Language2 { get; set; }

        /// <summary>The third content-language code, paired with <see cref="Name3" />.</summary>
        public string? Language3 { get; set; }

        /// <summary>The category's member name: <c>Live</c> or <c>Sandbox</c>.</summary>
        public string Category { get; set; } = null!;

        /// <summary>The live tenant a sandbox derives from.</summary>
        public int? LiveTenantId { get; set; }

        /// <summary>The state's member name.</summary>
        public string State { get; set; } = null!;

        /// <summary>The operator's reason for the last transition.</summary>
        public string? StateReason { get; set; }

        /// <summary>When the state last changed.</summary>
        public DateTimeOffset StateChangedAt { get; set; }

        /// <summary>Who changed the state last: a subject, a client id, or <c>migrator</c>.</summary>
        public string? StateChangedBy { get; set; }

        /// <summary>The database server: a host, or host and port.</summary>
        public string Server { get; set; } = null!;

        /// <summary>The database name.</summary>
        public string Database { get; set; } = null!;

        /// <summary>The name of the configured credential profile the tenant's connections use.</summary>
        public string CredentialProfile { get; set; } = null!;

        /// <summary>Non-secret, distribution-defined JSON.</summary>
        public string? Properties { get; set; }

        /// <summary>A fresh value on every update, so a reload can tell which rows changed.</summary>
        public Guid Version { get; set; }

        /// <summary>When the row was inserted.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>When the row was last updated.</summary>
        public DateTimeOffset ModifiedAt { get; set; }
    }

    /// <summary>The single row of <c>catalog.CatalogState</c>, whose version every catalog write changes.</summary>
    internal sealed class CatalogStateRow
    {
        /// <summary>Always 1.</summary>
        public int Id { get; set; }

        /// <summary>A fresh value on every catalog write; the refresh probe compares it.</summary>
        public Guid Version { get; set; }

        /// <summary>When the catalog was last written.</summary>
        public DateTimeOffset ModifiedAt { get; set; }
    }

    /// <summary>A row of <c>catalog.TenantMemberships</c>: one subject's membership hint in one tenant.</summary>
    internal sealed class CatalogTenantMembership
    {
        /// <summary>The identity subject or a service account's client id, compared byte for byte.</summary>
        public string Subject { get; set; } = null!;

        /// <summary>The tenant.</summary>
        public int TenantId { get; set; }

        /// <summary>Whether the subject's user in the tenant is active.</summary>
        public bool IsActive { get; set; }

        /// <summary>When the hint was last written.</summary>
        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>A row of <c>catalog.Sessions</c>: one distribution session, the store behind the session cookie.</summary>
    internal sealed class CatalogSession
    {
        /// <summary>The session key: base64url of 32 random bytes; never leaves the store and the cookie.</summary>
        public string Key { get; set; } = null!;

        /// <summary>The session's name for listeners, hub groups and logs: base64url of the first 16 bytes of the key's SHA-256.</summary>
        public string Handle { get; set; } = null!;

        /// <summary>The identity subject.</summary>
        public string Subject { get; set; } = null!;

        /// <summary>The identity server's session id.</summary>
        public string? Sid { get; set; }

        /// <summary>The Data-Protection-encrypted authentication ticket.</summary>
        public byte[] Ticket { get; set; } = null!;

        /// <summary>The compare-and-set counter of the token refresh.</summary>
        public int TokensVersion { get; set; }

        /// <summary>When the session was created.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>When the session was last renewed.</summary>
        public DateTimeOffset RenewedAt { get; set; }

        /// <summary>When the session expires: the last renewal plus the idle lifetime, capped by the absolute lifetime.</summary>
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
