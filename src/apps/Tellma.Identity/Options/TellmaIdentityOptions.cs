// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Root configuration for the identity engine, bound from the host's
    ///     <c>TellmaIdentity</c> configuration section (or configured in code by an in-proc
    ///     host). Validated at startup by <see cref="TellmaIdentityOptionsValidator" />.
    /// </summary>
    public sealed class TellmaIdentityOptions
    {
        /// <summary>The hosting shape. Standalone by default.</summary>
        public TellmaIdentityDeploymentMode Mode { get; set; }

        /// <summary>
        ///     The OpenID Connect issuer. Standalone: the authority's own origin (for example
        ///     <c>https://identity.tellma.com</c>). In-proc: the distribution's origin plus the
        ///     path base (for example <c>https://acme.app.tellma.com/id</c>).
        /// </summary>
        public Uri? Issuer { get; set; }

        /// <summary>
        ///     The reserved path base the engine is mounted at in in-proc mode (for example
        ///     <c>/id</c>). Empty in standalone mode.
        /// </summary>
        public string PathBase { get; set; } = string.Empty;

        /// <summary>
        ///     The SQL Server connection string for the identity store. Alternatively an in-proc
        ///     host supplies <see cref="ConfigureDbContext" /> to point the store at its own
        ///     database (the engine's tables live in the dedicated
        ///     <see cref="TellmaIdentityConstants.Schema" /> schema).
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        ///     Code-only hook for in-proc hosts to configure the identity <c>DbContext</c>
        ///     themselves (provider, connection, interceptors). When set,
        ///     <see cref="ConnectionString" /> is ignored.
        /// </summary>
        public Action<IServiceProvider, DbContextOptionsBuilder>? ConfigureDbContext { get; set; }

        /// <summary>
        ///     Offer password sign-in. Off by default: a passkey-first system with an email-code
        ///     recovery path needs no standing reusable secret. Enabled only when a distribution's
        ///     policy requires it.
        /// </summary>
        public bool EnablePasswordSignIn { get; set; }

        /// <summary>
        ///     The WebAuthn Relying Party ID passkeys are scoped to. Defaults to the issuer host:
        ///     the authority origin in standalone mode (one passkey works across every
        ///     distribution), the distribution host in in-proc mode.
        /// </summary>
        public string? PasskeyServerDomain { get; set; }

        /// <summary>Token lifetimes.</summary>
        public TellmaIdentityLifetimeOptions Lifetimes { get; } = new TellmaIdentityLifetimeOptions();

        /// <summary>Signing and encryption key material.</summary>
        public TellmaIdentityKeyOptions Keys { get; } = new TellmaIdentityKeyOptions();

        /// <summary>Outgoing email transport.</summary>
        public TellmaIdentityEmailOptions Email { get; } = new TellmaIdentityEmailOptions();

        /// <summary>Data Protection key-ring configuration (standalone mode only).</summary>
        public TellmaIdentityDataProtectionOptions DataProtection { get; } = new TellmaIdentityDataProtectionOptions();

        /// <summary>Mutual-TLS opt-in for sender-constrained tokens.</summary>
        public TellmaIdentityMutualTlsOptions MutualTls { get; } = new TellmaIdentityMutualTlsOptions();

        /// <summary>External login providers (Google, Microsoft).</summary>
        public TellmaIdentityExternalProviderOptions ExternalProviders { get; } = new TellmaIdentityExternalProviderOptions();

        /// <summary>Startup seeding: migrations, platform clients, and bootstrap identities.</summary>
        public TellmaIdentitySeedOptions Seed { get; } = new TellmaIdentitySeedOptions();

        /// <summary>Development affordances; all off by default.</summary>
        public TellmaIdentityDevelopmentOptions Development { get; } = new TellmaIdentityDevelopmentOptions();

        /// <summary>
        ///     The route prefix derived from <see cref="PathBase" /> (no slashes), prepended to
        ///     every engine route and protocol endpoint in in-proc mode.
        /// </summary>
        internal string RoutePrefix => PathBase.Trim('/');
    }
}
