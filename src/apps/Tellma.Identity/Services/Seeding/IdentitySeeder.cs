// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Services.Seeding
{
    /// <summary>
    ///     Idempotent startup seeding: the store check (with pending migrations applied first when
    ///     enabled), the fixed scope catalog with its platform audiences, the seeded platform
    ///     clients, the seeded distributions, and the bootstrap identities (dev admin in
    ///     Development; break-glass admin into an empty store).
    /// </summary>
    /// <param name="context">The identity store.</param>
    /// <param name="storeCheck">The store check that runs before anything is written.</param>
    /// <param name="converger">
    ///     The registration write path onboarding shares, which distribution entries, platform
    ///     clients and scopes all converge through, safe against instances starting together.
    /// </param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="environment">The host environment (gates the dev admin).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Progress logging.</param>
    public sealed class IdentitySeeder(
        TellmaIdentityDbContext context,
        IdentityStoreCheck storeCheck,
        ClientRegistrationConverger converger,
        UserManager<TellmaIdentityUser> userManager,
        IOptions<TellmaIdentityOptions> options,
        IHostEnvironment environment,
        IAuditLogger auditLogger,
        TimeProvider timeProvider,
        ILogger<IdentitySeeder> logger)
    {
        /// <summary>Runs the full seed pass.</summary>
        /// <param name="cancellationToken">Aborts seeding.</param>
        /// <returns>A task that completes when seeding finishes.</returns>
        /// <exception cref="IdentityStoreCheckException">The store failed the check; nothing was written.</exception>
        public async Task SeedAsync(CancellationToken cancellationToken)
        {
            TellmaIdentityOptions engineOptions = options.Value;

            // The store check comes first, so a store in the wrong collation, or one that is
            // missing or behind, stops the host before any row is written (spec 0010 §A.11).
            await storeCheck.CheckAsync(cancellationToken);

            await EnsureScopesAsync(engineOptions, cancellationToken);

            // Platform clients first — the client list's and the interim agent clients — and
            // distributions after: a distribution grants its origin to the seeded clients that
            // call distribution APIs, so those must exist by then, in whatever order the
            // configuration lists the entries. A disabled entry is skipped, never deleted.
            TellmaIdentitySeedClientOptions[] enabled =
                [.. engineOptions.Seed.ListEntries().Select(static entry => entry.Client).Where(static client => client.Enabled)];
            foreach (TellmaIdentitySeedClientOptions client in enabled)
            {
                if (client.Kind != TellmaIdentitySeedClientKind.Distribution)
                {
                    await EnsureClientAsync(client, cancellationToken);
                }
            }

            foreach (TellmaIdentitySeedClientOptions client in enabled)
            {
                if (client.Kind == TellmaIdentitySeedClientKind.Distribution)
                {
                    await EnsureDistributionAsync(client, cancellationToken);
                }
            }

            await EnsureDevAdminAsync(engineOptions, cancellationToken);
            await EnsureBootstrapAdminAsync(engineOptions, cancellationToken);
        }

        /// <summary>Seeds the fixed scope catalog and its platform audiences.</summary>
        private async Task EnsureScopesAsync(TellmaIdentityOptions engineOptions, CancellationToken cancellationToken)
        {
            string issuerOrigin = engineOptions.Issuer!.GetLeftPart(UriPartial.Authority);

            // In in-proc mode the single distribution's API audience is its own origin and is
            // known at startup; in standalone mode per-distribution audiences arrive through
            // provisioning.
            string[] apiResources = engineOptions.Mode == TellmaIdentityDeploymentMode.InProc ? [issuerOrigin] : [];

            await EnsureScopeAsync(TellmaIdentityConstants.ApiScope, "Call a distribution API", apiResources, cancellationToken);
            await EnsureScopeAsync(TellmaIdentityConstants.IdentityScope, "Call the identity server management API", [issuerOrigin], cancellationToken);

            // The control-plane scope and its audience exist only in standalone mode, where the
            // operator surface lives; in-proc deployments never advertise or grant it.
            if (engineOptions.Mode != TellmaIdentityDeploymentMode.InProc)
            {
                await EnsureScopeAsync(
                    TellmaIdentityConstants.ControlPlaneScope,
                    "Call the control-plane admin surface",
                    [TellmaIdentityConstants.ControlPlaneAudience],
                    cancellationToken);
            }
        }

        /// <summary>Creates a scope or unions the given resources into an existing one.</summary>
        private async Task EnsureScopeAsync(
            string name, string displayName, IReadOnlyList<string> resources, CancellationToken cancellationToken)
        {
            // Resources appended at runtime (per-distribution audiences) are preserved.
            await converger.ConvergeScopeAsync(name, displayName, resources, cancellationToken);
        }

        /// <summary>Creates or refreshes one seeded platform client, keeping the audiences onboarding granted it.</summary>
        private async Task EnsureClientAsync(TellmaIdentitySeedClientOptions seed, CancellationToken cancellationToken)
        {
            if (await converger.ConvergeSeededClientAsync(seed, cancellationToken) == ClientConvergence.Created)
            {
                IdentitySeederLog.SeededClient(logger, seed.ClientId!);
            }
        }

        /// <summary>
        ///     Converges one distribution entry's two clients through onboarding's own steps, and
        ///     records an audit event when that changed anything.
        /// </summary>
        private async Task EnsureDistributionAsync(TellmaIdentitySeedClientOptions seed, CancellationToken cancellationToken)
        {
            DistributionClientRegistration registration = ClientDescriptorFactory.SeededDistribution(seed);
            DistributionConvergence convergence = await converger.ConvergeDistributionAsync(registration, cancellationToken);

            string origin = registration.Origin.GetLeftPart(UriPartial.Authority);
            if (!convergence.Changed)
            {
                IdentitySeederLog.DistributionUnchanged(logger, registration.Slug);
                return;
            }

            IdentitySeederLog.DistributionApplied(
                logger, registration.Slug, origin, convergence.Client, convergence.ServiceClient);

            // The outcome per client and per grant, never a secret: a rotation shows as an update
            // of the client whose secret changed.
            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.DistributionSeedApplied,
                    ClientId = registration.Slug,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        origin,
                        client = ConvergenceName(convergence.Client),
                        serviceClient = ConvergenceName(convergence.ServiceClient),
                        audienceAdded = convergence.AudienceAdded,
                        grantsAdded = convergence.GrantsAdded,
                    }),
                },
                cancellationToken);
        }

        /// <summary>The audit-detail spelling of a convergence outcome.</summary>
        private static string ConvergenceName(ClientConvergence convergence)
        {
            return convergence switch
            {
                ClientConvergence.Unchanged => "unchanged",
                ClientConvergence.Created => "created",
                ClientConvergence.Updated => "updated",
                _ => throw new ArgumentOutOfRangeException(nameof(convergence), convergence, null),
            };
        }

        /// <summary>Seeds the Development-only admin identity with its fixed subject.</summary>
        private async Task EnsureDevAdminAsync(TellmaIdentityOptions engineOptions, CancellationToken cancellationToken)
        {
            // Strictly Development: the seeded admin (and its sign-in-page enrollment affordance)
            // is the only way local authentication differs from a deployed instance.
            if (!engineOptions.Seed.DevAdmin.Enabled || !environment.IsDevelopment())
            {
                return;
            }

            string email = engineOptions.Seed.DevAdmin.Email;
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                return;
            }

            TellmaIdentityUser user = new()
            {
                Id = engineOptions.Seed.DevAdmin.Subject,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Development Administrator",
                Locale = "en",
                LifecycleState = UserLifecycleState.Active,
                CreatedUtc = timeProvider.GetUtcNow(),
            };

            IdentityResult result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to seed the development admin: " + string.Join("; ", result.Errors.Select(static e => e.Description)));
            }

            await auditLogger.LogAsync(
                new AuditEventEntry { Action = AuditActions.DevAdminSeeded, Subject = user.Id },
                cancellationToken);
        }

        /// <summary>Seeds the break-glass administrator into an empty user store.</summary>
        private async Task EnsureBootstrapAdminAsync(TellmaIdentityOptions engineOptions, CancellationToken cancellationToken)
        {
            string? email = engineOptions.Seed.Bootstrap.AdminEmail;
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            // Only ever into an empty store: once anyone exists, administration happens through
            // the normal surfaces and the setup token is dead.
            if (await context.Users.AnyAsync(cancellationToken))
            {
                return;
            }

            TellmaIdentityUser user = new()
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Administrator",
                Locale = "en",
                LifecycleState = UserLifecycleState.Active,
                CreatedUtc = timeProvider.GetUtcNow(),
            };

            IdentityResult result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to seed the break-glass administrator: " + string.Join("; ", result.Errors.Select(static e => e.Description)));
            }

            await auditLogger.LogAsync(
                new AuditEventEntry { Action = AuditActions.BootstrapAdminSeeded, Subject = user.Id },
                cancellationToken);
        }
    }

    /// <summary>Source-generated log messages for <see cref="IdentitySeeder" />.</summary>
    internal static partial class IdentitySeederLog
    {
        /// <summary>A platform client was seeded.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="clientId">The seeded client id.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Seeded platform client {ClientId}.")]
        public static partial void SeededClient(ILogger logger, string clientId);

        /// <summary>A distribution entry changed the store.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="origin">The distribution origin.</param>
        /// <param name="clientOutcome">The BFF client's outcome.</param>
        /// <param name="serviceClientOutcome">The backend client's outcome.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Applied distribution seed {Slug} at {Origin}: client {ClientOutcome}, service client {ServiceClientOutcome}.")]
        public static partial void DistributionApplied(
            ILogger logger, string slug, string origin, ClientConvergence clientOutcome, ClientConvergence serviceClientOutcome);

        /// <summary>A distribution entry was already converged.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="slug">The distribution slug.</param>
        [LoggerMessage(Level = LogLevel.Debug, Message = "Distribution seed {Slug} is already converged.")]
        public static partial void DistributionUnchanged(ILogger logger, string slug);
    }
}
