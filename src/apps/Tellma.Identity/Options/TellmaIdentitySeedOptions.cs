// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Startup seeding: the store check, migrations, scopes, platform clients, and bootstrap
    ///     identities.
    /// </summary>
    public sealed class TellmaIdentitySeedOptions
    {
        /// <summary>
        ///     Create a missing store and apply pending EF Core migrations at startup, before the
        ///     store check reads the migration history. Only development and test hosts turn this
        ///     on: a deployed engine holds no DDL rights, and its store is migrated by the
        ///     distribution's migrator or by an operator. Turning it on requires the host to
        ///     reference the engine's migrations assembly.
        /// </summary>
        public bool ApplyMigrations { get; set; }

        /// <summary>
        ///     The command that migrates this host's store, quoted by the error a failed store check
        ///     raises when the store is missing, empty, or behind the engine's last migration — for
        ///     example the <c>dotnet ef database update</c> invocation a standalone operator runs, or
        ///     a distribution migrator's <c>migrate</c> command. Optional; without it the error
        ///     still names the database and the missing migration.
        /// </summary>
        public string? MigrateCommand { get; set; }

        /// <summary>
        ///     Platform clients seeded idempotently at startup (CLI, native apps, control plane, and
        ///     distributions).
        /// </summary>
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

        /// <summary>
        ///     A distribution's two clients from one entry, registered by onboarding's own steps: the
        ///     confidential BFF client named by the slug, and its <c>&lt;slug&gt;-svc</c> backend
        ///     machine client without token exchange. The entry's origin joins the distribution
        ///     API's audiences and is granted to every seeded client that calls distribution APIs.
        /// </summary>
        Distribution = 3,
    }

    /// <summary>One platform client seeded at startup.</summary>
    public sealed class TellmaIdentitySeedClientOptions
    {
        /// <summary>The longest distribution slug a <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry accepts.</summary>
        public const int MaxDistributionSlugLength = 15;

        /// <summary>The path, under the origin, of a distribution BFF's default back-channel logout endpoint.</summary>
        public const string DefaultBackchannelLogoutPath = "/bff/backchannel-logout";

        /// <summary>
        ///     The client identifier, for example <c>tellma-cli</c>. For a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry it is the distribution's
        ///     slug: lowercase letters, digits and hyphens, starting with a letter, at most
        ///     <see cref="MaxDistributionSlugLength" /> characters.
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        ///     Human-readable display name shown on consent and device pages. Ignored for a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry, whose clients carry
        ///     onboarding's names.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>The client archetype, which determines type, grants, and permissions.</summary>
        public TellmaIdentitySeedClientKind Kind { get; set; }

        /// <summary>
        ///     Redirect URIs. For the CLI archetype a portless loopback URI (for example
        ///     <c>http://127.0.0.1/callback</c>) accepts any ephemeral port at runtime. Ignored for
        ///     a <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry, whose redirect URIs
        ///     derive from <see cref="Origin" />.
        /// </summary>
        public IList<string> RedirectUris { get; } = [];

        /// <summary>
        ///     The client secret for confidential archetypes, sourced from the deployment's secret
        ///     store — never generated and printed at seed time. For a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry it is the BFF client's
        ///     secret, required outside the Development environment. In Development a host whose
        ///     configuration has none supplies a per-boot secret itself; the seeder never generates
        ///     one, so an entry that reaches seeding without a secret fails it.
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>
        ///     Additional resource (audience) permissions granted to the client. Ignored for a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry, whose resources are
        ///     fixed by onboarding's descriptors.
        /// </summary>
        public IList<string> Resources { get; } = [];

        /// <summary>
        ///     Ask the user to approve this client's scopes the first time, and record the grant.
        ///     Off by default because the seeded clients are first-party — the user has already
        ///     chosen the product they are signing in to, so a consent screen would be noise. A
        ///     client acting for someone else is the case this exists for. Ignored for a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry, whose BFF client is
        ///     first-party.
        /// </summary>
        public bool RequireConsent { get; set; }

        /// <summary>
        ///     The distribution's browser origin: absolute and path-less, <c>https</c> outside the
        ///     Development environment. Required for, and only read by, a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry.
        /// </summary>
        public string? Origin { get; set; }

        /// <summary>
        ///     The BFF client's back-channel logout endpoint. Read only by a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry, where it defaults to
        ///     <c>{Origin}/bff/backchannel-logout</c>.
        /// </summary>
        public string? BackchannelLogoutUri { get; set; }

        /// <summary>
        ///     The <c>&lt;slug&gt;-svc</c> backend machine client's secret, sourced from the
        ///     deployment's secret store. Read only by a
        ///     <see cref="TellmaIdentitySeedClientKind.Distribution" /> entry, and required outside
        ///     the Development environment on the same terms as <see cref="ClientSecret" />.
        /// </summary>
        public string? ServiceClientSecret { get; set; }
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
