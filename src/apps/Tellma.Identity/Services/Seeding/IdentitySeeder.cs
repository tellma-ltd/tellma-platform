// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Tellma.Identity.Data;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Services.Seeding
{
    /// <summary>
    ///     Idempotent startup seeding: pending migrations (when enabled), the fixed scope catalog
    ///     with its platform audiences, the seeded platform clients, and the bootstrap identities
    ///     (dev admin in Development; break-glass admin into an empty store).
    /// </summary>
    /// <param name="context">The identity store.</param>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="scopeManager">The OpenIddict scope manager.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="environment">The host environment (gates the dev admin).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Progress logging.</param>
    public sealed class IdentitySeeder(
        TellmaIdentityDbContext context,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
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
        public async Task SeedAsync(CancellationToken cancellationToken)
        {
            TellmaIdentityOptions engineOptions = options.Value;

            if (engineOptions.Seed.ApplyMigrations)
            {
                IdentitySeederLog.ApplyingMigrations(logger);
                await context.Database.MigrateAsync(cancellationToken);
            }

            await EnsureScopesAsync(engineOptions, cancellationToken);

            foreach (TellmaIdentitySeedClientOptions client in engineOptions.Seed.Clients)
            {
                await EnsureClientAsync(engineOptions, client, cancellationToken);
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
            await EnsureScopeAsync(TellmaIdentityConstants.ControlPlaneScope, "Call the control-plane admin surface", [TellmaIdentityConstants.ControlPlaneAudience], cancellationToken);
        }

        /// <summary>Creates a scope or unions the given resources into an existing one.</summary>
        private async Task EnsureScopeAsync(
            string name, string displayName, IReadOnlyList<string> resources, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(name, cancellationToken);
            if (scope is null)
            {
                OpenIddictScopeDescriptor descriptor = new() { Name = name, DisplayName = displayName };
                descriptor.Resources.UnionWith(resources);
                await scopeManager.CreateAsync(descriptor, cancellationToken);
                return;
            }

            // Preserve resources appended at runtime (per-distribution audiences).
            OpenIddictScopeDescriptor existing = new();
            await scopeManager.PopulateAsync(existing, scope, cancellationToken);
            if (!resources.All(existing.Resources.Contains))
            {
                existing.Resources.UnionWith(resources);
                await scopeManager.UpdateAsync(scope, existing, cancellationToken);
            }
        }

        /// <summary>Creates or refreshes one seeded platform client.</summary>
        private async Task EnsureClientAsync(
            TellmaIdentityOptions engineOptions, TellmaIdentitySeedClientOptions seed, CancellationToken cancellationToken)
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.SeededClient(engineOptions, seed);

            object? existing = await applicationManager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
            if (existing is null)
            {
                await applicationManager.CreateAsync(descriptor, cancellationToken);
                IdentitySeederLog.SeededClient(logger, descriptor.ClientId!);
                return;
            }

            // Re-seeding must not wipe the per-distribution resource permissions granted to
            // platform clients at provisioning time.
            OpenIddictApplicationDescriptor current = new();
            await applicationManager.PopulateAsync(current, existing, cancellationToken);
            foreach (string permission in current.Permissions)
            {
                if (permission.StartsWith(OpenIddictConstants.Permissions.Prefixes.Resource, StringComparison.Ordinal))
                {
                    descriptor.Permissions.Add(permission);
                }
            }

            await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
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
        /// <summary>Migrations are about to be applied.</summary>
        /// <param name="logger">The logger.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Applying pending identity store migrations.")]
        public static partial void ApplyingMigrations(ILogger logger);

        /// <summary>A platform client was seeded.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="clientId">The seeded client id.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Seeded platform client {ClientId}.")]
        public static partial void SeededClient(ILogger logger, string clientId);
    }
}
