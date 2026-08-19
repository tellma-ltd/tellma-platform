// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;
using Tellma.Identity.Hosting;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Tellma.Identity.E2E.Infrastructure.IdentityServerFixture))]

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     Runs the real identity engine on Kestrel at an ephemeral loopback port (a real socket a
    ///     browser can reach — the in-memory TestServer cannot), backed by a fresh SQL Server
    ///     database and the in-process capturing sender E2E tests read codes and links from.
    ///     Concrete fixtures pick the hosting shape (standalone at the root, or in-proc under the
    ///     reserved path base).
    /// </summary>
    public abstract class IdentityServerFixtureBase : IAsyncLifetime
    {
        private MsSqlContainer? _container;
        private WebApplication? _app;

        /// <summary>The supplied server's connection string, when not running a container.</summary>
        private string? _masterConnectionString;

        /// <summary>The database this fixture created, so teardown can drop it again.</summary>
        private string? _databaseName;

        /// <summary>The base address the browser navigates to.</summary>
        public string BaseAddress { get; private set; } = string.Empty;

        /// <summary>
        ///     The same host reached by IP rather than by name. A browser treats it as a different
        ///     origin, so it stands in for a client's own site in flows that end in a cross-origin
        ///     redirect — while still being a socket that answers, which a made-up address is not.
        /// </summary>
        public string CrossOriginAddress { get; private set; } = string.Empty;

        /// <summary>The path <see cref="CrossOriginAddress" /> serves as a stand-in client callback.</summary>
        public const string CallbackPath = "/e2e/callback";

        /// <summary>The user whose session <see cref="SignedInStorageStateAsync" /> hands out.</summary>
        public const string SharedSessionEmail = "e2e-session@example.com";

        /// <summary>Serializes the one sign-in, so two tests cannot both perform it.</summary>
        private readonly SemaphoreSlim _signInGate = new(1, 1);

        /// <summary>The captured session, once established.</summary>
        private string? _storageState;

        /// <summary>The captured outbound email (codes, links).</summary>
        public CapturingEmailSender Emails { get; } = new CapturingEmailSender();

        /// <summary>The deployment mode configured on the engine.</summary>
        protected abstract string Mode { get; }

        /// <summary>The reserved path base ("" standalone, "/id" in-proc).</summary>
        protected abstract string PathPrefix { get; }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            string connectionString = await StartDatabaseAsync();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(
                new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TellmaIdentity:Mode"] = Mode,
                ["TellmaIdentity:PathBase"] = PathPrefix,
                ["TellmaIdentity:Issuer"] = "http://127.0.0.1" + PathPrefix,
                ["TellmaIdentity:ConnectionString"] = connectionString,
                ["TellmaIdentity:Keys:Signing:Source"] = "DevelopmentSelfSigned",
                ["TellmaIdentity:Keys:Encryption:Source"] = "DevelopmentSelfSigned",
                ["TellmaIdentity:Development:AllowDevelopmentCertificates"] = "true",
                ["TellmaIdentity:Development:AllowInsecureHttp"] = "true",
                ["TellmaIdentity:EnablePasswordSignIn"] = "true",
                ["TellmaIdentity:Seed:ApplyMigrations"] = "true",
                ["TellmaIdentity:Seed:DevAdmin:Enabled"] = "true",
                // WebAuthn requires a domain RP id, so the browser reaches the loopback host as
                // "localhost" (special-cased by WebAuthn) rather than the 127.0.0.1 IP literal.
                ["TellmaIdentity:PasskeyServerDomain"] = "localhost",
                // Registering the providers costs a client id and nothing else — no request ever
                // leaves for them here. It is what makes the sign-in page's federated buttons and
                // the whole of the external-logins page exist to be scanned; without it those are
                // the one surface the accessibility gate silently skips.
                ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "e2e-google-client-id",
                ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "e2e-google-client-secret",
                ["TellmaIdentity:ExternalProviders:Microsoft:ClientId"] = "e2e-microsoft-client-id",
                ["TellmaIdentity:ExternalProviders:Microsoft:ClientSecret"] = "e2e-microsoft-client-secret",
            });
            builder.Services.AddTellmaIdentity(builder.Configuration.GetSection("TellmaIdentity"));
            builder.Services.RemoveAll<IEmailSender>();
            builder.Services.AddSingleton<IEmailSender>(Emails);

            _app = builder.Build();
            _app.UseRouting();
            _app.UseTellmaIdentity();
            _app.UseAuthentication();
            _app.UseAuthorization();

            // The inline test host has no static-web-assets manifest, so serve the engine's RCL
            // assets from their source folder under _content/Tellma.Identity.
            _app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/_content/Tellma.Identity",
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(FindEngineWwwRoot()),
            });

            if (Mode == "InProc")
            {
                // The "distribution's" own surface, mirroring the in-proc composition shape.
                _app.MapGet("/", static () => "Distribution host");
            }

            _app.MapTellmaIdentity();

            // Stands in for a relying party's own landing page. It is this same server, so it
            // always answers; reached by IP it is nonetheless a different origin to the browser,
            // which is what makes it a faithful target for a cross-origin protocol redirect.
            _app.MapGet(CallbackPath, static () => "callback");

            await _app.StartAsync();

            IServerAddressesFeature addresses = _app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!;
            CrossOriginAddress = addresses.Addresses.First();
            // Reach the loopback host as "localhost" so the WebAuthn RP id resolves.
            BaseAddress = CrossOriginAddress.Replace("127.0.0.1", "localhost", StringComparison.Ordinal);
        }

        /// <summary>Creates an active, email-confirmed user directly in the running host's store.</summary>
        /// <param name="email">The user's email.</param>
        /// <returns>A task that completes when the user exists.</returns>
        public async Task CreateActiveUserAsync(string email)
        {
            await using AsyncServiceScope scope = _app!.Services.CreateAsyncScope();
            Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser>>();

            if (await userManager.FindByEmailAsync(email) is not null)
            {
                return;
            }

            await userManager.CreateAsync(new Data.TellmaIdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = email.Split('@')[0],
                Locale = "en",
                LifecycleState = Data.UserLifecycleState.Active,
                CreatedUtc = DateTimeOffset.UtcNow,
            });
        }

        /// <summary>
        ///     A signed-in browser session, established once and handed to every test that needs
        ///     one.
        ///     <para>
        ///         Sign-in codes are rate-limited per IP address, and every test in this suite
        ///         reaches the server from the same loopback address — so a suite that signed in
        ///         once per test would exhaust that budget partway through and then quietly stop
        ///         receiving codes. Reusing one session is also several seconds faster per test.
        ///     </para>
        /// </summary>
        /// <param name="browser">The browser to run the one sign-in in.</param>
        /// <returns>Storage state to hand to <c>NewContextAsync</c>.</returns>
        public async Task<string> SignedInStorageStateAsync(Microsoft.Playwright.IBrowser browser)
        {
            ArgumentNullException.ThrowIfNull(browser);

            await _signInGate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                if (_storageState is not null)
                {
                    return _storageState;
                }

                await CreateActiveUserAsync(SharedSessionEmail);

                await using Microsoft.Playwright.IBrowserContext context = await browser.NewContextAsync(
                    new Microsoft.Playwright.BrowserNewContextOptions { BaseURL = BaseAddress });
                Microsoft.Playwright.IPage page = await context.NewPageAsync();
                await PasskeyCeremonies.SignInWithEmailCodeAsync(this, page, string.Empty, SharedSessionEmail);

                _storageState = await context.StorageStateAsync();
                return _storageState;
            }
            finally
            {
                _signInGate.Release();
            }
        }

        /// <summary>
        ///     Issues an invitation link for a user, the way the bulk-invite API does, so a browser
        ///     test can land on the invitation page the way an invited person does.
        /// </summary>
        /// <param name="email">The invited user, who must hold no credential yet.</param>
        /// <param name="returnUrl">Where accepting it should send the user; null keeps them here.</param>
        /// <param name="createdByClientId">The client the destination is validated against.</param>
        /// <returns>The single-use invitation token.</returns>
        public async Task<string> IssueInvitationTokenAsync(
            string email, string? returnUrl = null, string? createdByClientId = null)
        {
            await using AsyncServiceScope scope = _app!.Services.CreateAsyncScope();
            Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser>>();
            Services.Tokens.IOneTimeTokenService tokens =
                scope.ServiceProvider.GetRequiredService<Services.Tokens.IOneTimeTokenService>();

            Data.TellmaIdentityUser user = (await userManager.FindByEmailAsync(email))!;
            return await tokens.IssueAsync(
                user.Id,
                Data.Entities.SingleUseCodePurpose.Invitation,
                TimeSpan.FromHours(1),
                returnUrl,
                createdByClientId,
                TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Registers a third-party client that requires explicit consent. Every provisioned
        ///     client is first-party and implicit, so without one of these the consent screen — and
        ///     everything the browser does with it — has no caller.
        /// </summary>
        /// <param name="clientId">The client id.</param>
        /// <param name="displayName">The name the consent screen shows.</param>
        /// <param name="redirectUri">The callback the grant redirects to.</param>
        /// <returns>A task that completes when the client is registered.</returns>
        public async Task CreateConsentClientAsync(string clientId, string displayName, string redirectUri)
        {
            await using AsyncServiceScope scope = _app!.Services.CreateAsyncScope();
            IOpenIddictApplicationManager applications =
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            if (await applications.FindByClientIdAsync(clientId) is not null)
            {
                return;
            }

            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                DisplayName = displayName,
                ClientType = OpenIddictConstants.ClientTypes.Public,
                ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
                RedirectUris = { new Uri(redirectUri) },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                },
                Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange },
            });
        }

        /// <summary>
        ///     Registers a first-party client whose grants need no consent screen, so the browser
        ///     goes from the sign-in form to the client's callback in one uninterrupted navigation.
        ///     That is the shape almost every real client has, and the only shape in which the
        ///     sign-in page's own submission is the navigation that has to reach the callback.
        /// </summary>
        /// <param name="clientId">The client id.</param>
        /// <param name="redirectUri">The callback the authorization redirects to.</param>
        /// <returns>A task that completes when the client is registered.</returns>
        public async Task CreateFirstPartyClientAsync(string clientId, string redirectUri)
        {
            await using AsyncServiceScope scope = _app!.Services.CreateAsyncScope();
            IOpenIddictApplicationManager applications =
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            if (await applications.FindByClientIdAsync(clientId) is not null)
            {
                return;
            }

            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = clientId,
                DisplayName = clientId,
                ClientType = OpenIddictConstants.ClientTypes.Public,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                RedirectUris = { new Uri(redirectUri) },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                },
                Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange },
            };

            Services.Provisioning.TellmaClientProperties.Set(
                descriptor.Properties, Services.Provisioning.TellmaClientProperties.FirstParty, "true");

            await applications.CreateAsync(descriptor);
        }

        /// <summary>
        ///     Registers a first-party client at an origin, so an invitation it raises may name a
        ///     destination there. The origin property is what the return-url check reads; nothing
        ///     about the request is trusted.
        /// </summary>
        /// <param name="clientId">The client id.</param>
        /// <param name="origin">The origin the client receives users at.</param>
        /// <returns>A task that completes when the client is registered.</returns>
        public async Task CreateOriginClientAsync(string clientId, string origin)
        {
            await using AsyncServiceScope scope = _app!.Services.CreateAsyncScope();
            IOpenIddictApplicationManager applications =
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            if (await applications.FindByClientIdAsync(clientId) is not null)
            {
                return;
            }

            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = clientId,
                DisplayName = clientId,
                ClientType = OpenIddictConstants.ClientTypes.Public,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            };

            Services.Provisioning.TellmaClientProperties.Set(
                descriptor.Properties, Services.Provisioning.TellmaClientProperties.Origin, origin);
            Services.Provisioning.TellmaClientProperties.Set(
                descriptor.Properties, Services.Provisioning.TellmaClientProperties.FirstParty, "true");

            await applications.CreateAsync(descriptor);
        }

        /// <summary>
        ///     Records a device-bound passkey for a user directly in the store, so a browser test
        ///     can model a user who <em>owns</em> a hardware key without having to hold one in the
        ///     ceremony. Removing a virtual authenticator destroys its credentials, so ownership
        ///     and availability cannot both be arranged through CDP alone.
        /// </summary>
        /// <param name="email">The user to give a hardware key.</param>
        /// <returns>A task that completes when the credential is recorded.</returns>
        public async Task AddDeviceBoundPasskeyAsync(string email)
        {
            await using AsyncServiceScope scope = _app!.Services.CreateAsyncScope();
            Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser>>();

            Data.TellmaIdentityUser user = (await userManager.FindByEmailAsync(email))!;
            Microsoft.AspNetCore.Identity.UserPasskeyInfo passkey = new(
                credentialId: System.Security.Cryptography.RandomNumberGenerator.GetBytes(16),
                publicKey: System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
                createdAt: DateTimeOffset.UtcNow,
                signCount: 0,
                transports: null,
                isUserVerified: true,

                // Not backup-eligible is exactly what the engine classifies as device-bound.
                isBackupEligible: false,
                isBackedUp: false,
                attestationObject: [],
                clientDataJson: []);

            await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        }

        /// <summary>
        ///     Records an external link the way the sign-in callback does, so the account page has a
        ///     linked row to render — the badge, the address and the unlink control, none of which
        ///     exist on an unlinked row. The account is created first if it does not exist yet.
        /// </summary>
        /// <param name="email">The account to link, created if absent.</param>
        /// <param name="provider">The provider scheme, for example <c>Google</c>.</param>
        /// <param name="account">The address the provider asserted.</param>
        /// <returns>A task that completes when the link is stored.</returns>
        public async Task AddExternalLoginAsync(string email, string provider, string account)
        {
            // Seeding a link says nothing about when the account was made, and for the shared
            // session's address that moment is the first test to ask for a signed-in context —
            // which a test seeding its state up front has not done yet. Creating it here is
            // idempotent, and it is what keeps this independent of the order tests run in.
            await CreateActiveUserAsync(email);

            await using AsyncServiceScope scope = _app!.Services.CreateAsyncScope();
            Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Data.TellmaIdentityUser>>();

            // Still checked, because creation above reports failure by leaving nothing behind: an
            // address Identity's validator refuses would otherwise reach AddLoginAsync as a null
            // user and surface as a null-argument throw from inside the framework, naming a
            // parameter rather than the address that could not be made.
            Data.TellmaIdentityUser user = await userManager.FindByEmailAsync(email)
                ?? throw new InvalidOperationException($"No user holds the address '{email}'.");

            if (await userManager.FindByLoginAsync(provider, account) is null)
            {
                await userManager.AddLoginAsync(
                    user, new Microsoft.AspNetCore.Identity.UserLoginInfo(provider, account, account));
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);

            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }

            if (_container is not null)
            {
                // The container is discarded wholesale; no per-database cleanup needed.
                await _container.DisposeAsync();
                return;
            }

            // Running against a server the developer supplied (TELLMA_TEST_SQL): the database
            // outlives the process unless this drops it, and one accumulates per fixture per run.
            if (_masterConnectionString is not null && _databaseName is not null)
            {
                await using SqlConnection connection = new(_masterConnectionString);
                await connection.OpenAsync();
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText =
                    $"IF DB_ID('{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] "
                    + $"SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END";
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>Locates the engine project's source <c>wwwroot</c> by walking up to the repo root.</summary>
        private static string FindEngineWwwRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "src", "apps", "Tellma.Identity", "wwwroot");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate the Tellma.Identity wwwroot folder.");
        }

        /// <summary>Starts the test database (Testcontainers, or a supplied server) and returns its connection string.</summary>
        private async Task<string> StartDatabaseAsync()
        {
            string? overrideConnectionString = Environment.GetEnvironmentVariable("TELLMA_TEST_SQL");
            string masterConnectionString;
            if (!string.IsNullOrWhiteSpace(overrideConnectionString))
            {
                masterConnectionString = overrideConnectionString;
                _masterConnectionString = overrideConnectionString;
            }
            else
            {
                string image = Environment.GetEnvironmentVariable("TELLMA_TEST_SQL_IMAGE")
                    ?? "mcr.microsoft.com/mssql/server:2022-latest";
                _container = new MsSqlBuilder(image).Build();
                await _container.StartAsync(TestContext.Current.CancellationToken);
                masterConnectionString = _container.GetConnectionString();
            }

            string database = $"ide2e_{Guid.NewGuid():N}";
            _databaseName = database;
            await using (SqlConnection connection = new(masterConnectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{database}]";
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            return new SqlConnectionStringBuilder(masterConnectionString) { InitialCatalog = database }.ConnectionString;
        }
    }

    /// <summary>The standalone-shaped E2E host: the authority at the origin root.</summary>
    public sealed class IdentityServerFixture : IdentityServerFixtureBase
    {
        /// <inheritdoc />
        protected override string Mode => "Standalone";

        /// <inheritdoc />
        protected override string PathPrefix => string.Empty;
    }
}
