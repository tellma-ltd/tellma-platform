// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>Startup seeding: migrations, scopes, platform clients, and bootstrap identities.</summary>
    public sealed class TellmaIdentitySeedOptions
    {
        /// <summary>
        ///     Apply pending EF Core migrations at startup. Defaulted on in Development; explicit
        ///     in production deployments.
        /// </summary>
        public bool ApplyMigrations { get; set; }

        /// <summary>Platform clients seeded idempotently at startup (CLI, native apps, control plane).</summary>
        public IList<TellmaIdentitySeedClientOptions> Clients { get; } = [];

        /// <summary>Development-only seeded admin identity.</summary>
        public TellmaIdentityDevAdminOptions DevAdmin { get; } = new TellmaIdentityDevAdminOptions();

        /// <summary>Deployed-instance break-glass administrator bootstrap.</summary>
        public TellmaIdentityBootstrapOptions Bootstrap { get; } = new TellmaIdentityBootstrapOptions();
    }

    /// <summary>The archetype of a seeded platform client.</summary>
    public enum TellmaIdentitySeedClientKind
    {
        /// <summary>
        ///     The Tellma CLI: a public native client using Authorization Code + PKCE over a
        ///     loopback redirect, and the Device Authorization Grant when headless.
        /// </summary>
        Cli = 0,

        /// <summary>
        ///     A native app: a public client using Authorization Code + PKCE via the system
        ///     browser, and the Device Authorization Grant when no browser is available.
        /// </summary>
        Native = 1,

        /// <summary>The control plane: a confidential client credentials caller.</summary>
        ControlPlane = 2,
    }

    /// <summary>One platform client seeded at startup.</summary>
    public sealed class TellmaIdentitySeedClientOptions
    {
        /// <summary>The client identifier, for example <c>tellma-cli</c>.</summary>
        public string? ClientId { get; set; }

        /// <summary>Human-readable display name shown on consent and device pages.</summary>
        public string? DisplayName { get; set; }

        /// <summary>The client archetype, which determines type, grants, and permissions.</summary>
        public TellmaIdentitySeedClientKind Kind { get; set; }

        /// <summary>
        ///     Redirect URIs. For the CLI archetype a portless loopback URI (for example
        ///     <c>http://127.0.0.1/callback</c>) accepts any ephemeral port at runtime.
        /// </summary>
        public IList<string> RedirectUris { get; } = [];

        /// <summary>
        ///     The client secret for confidential archetypes, sourced from the deployment's secret
        ///     store — never generated and printed at seed time.
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>Additional resource (audience) permissions granted to the client.</summary>
        public IList<string> Resources { get; } = [];
    }

    /// <summary>The Development-only seeded admin identity.</summary>
    public sealed class TellmaIdentityDevAdminOptions
    {
        /// <summary>Seed the dev admin at startup. Only honored in the Development environment.</summary>
        public bool Enabled { get; set; }

        /// <summary>The dev admin's email/username.</summary>
        public string Email { get; set; } = "admin@localhost";

        /// <summary>
        ///     The dev admin's fixed subject identifier, so a distribution's own seed can map the
        ///     matching tenant admin onto the same <c>sub</c>.
        /// </summary>
        public string Subject { get; set; } = "00000000-0000-0000-0000-000000000001";
    }

    /// <summary>Break-glass administrator bootstrap for deployed instances.</summary>
    public sealed class TellmaIdentityBootstrapOptions
    {
        /// <summary>
        ///     The break-glass administrator's email. When set and the user store is empty, the
        ///     admin is seeded without credentials; the one-time setup token is the only way in.
        /// </summary>
        public string? AdminEmail { get; set; }

        /// <summary>
        ///     Hex-encoded SHA-256 hash of the one-time setup token. The token itself is delivered
        ///     through a secure channel (Key Vault secret / provisioning output) and never stored
        ///     or logged by the server.
        /// </summary>
        public string? SetupTokenSha256 { get; set; }
    }
}
