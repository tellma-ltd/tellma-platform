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
using Tellma.Identity.Hosting;
using Tellma.Identity.Services.Email;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Tellma.Identity.E2E.Infrastructure.IdentityServerFixture))]

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     Runs the real identity engine on Kestrel at an ephemeral loopback port (a real socket a
    ///     browser can reach — the in-memory TestServer cannot), backed by a fresh SQL Server
    ///     database and the in-process capturing email sink E2E tests read codes and links from.
    ///     Concrete fixtures pick the hosting shape (standalone at the root, or in-proc under the
    ///     reserved path base).
    /// </summary>
    public abstract class IdentityServerFixtureBase : IAsyncLifetime
    {
        private MsSqlContainer? _container;
        private WebApplication? _app;

        /// <summary>The base address the browser navigates to.</summary>
        public string BaseAddress { get; private set; } = string.Empty;

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
                ["TellmaIdentity:Development:UseEmailSink"] = "true",
                ["TellmaIdentity:EnablePasswordSignIn"] = "true",
                ["TellmaIdentity:Seed:ApplyMigrations"] = "true",
                ["TellmaIdentity:Seed:DevAdmin:Enabled"] = "true",
                // WebAuthn requires a domain RP id, so the browser reaches the loopback host as
                // "localhost" (special-cased by WebAuthn) rather than the 127.0.0.1 IP literal.
                ["TellmaIdentity:PasskeyServerDomain"] = "localhost",
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

            await _app.StartAsync();

            IServerAddressesFeature addresses = _app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!;
            // Reach the loopback host as "localhost" so the WebAuthn RP id resolves.
            BaseAddress = addresses.Addresses.First().Replace("127.0.0.1", "localhost", StringComparison.Ordinal);
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
                await _container.DisposeAsync();
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
