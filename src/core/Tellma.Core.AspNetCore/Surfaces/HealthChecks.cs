// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Surfaces
{
    /// <summary>The names and tag of the readiness checks.</summary>
    internal static class HealthCheckNames
    {
        /// <summary>The tag of every check <c>/health/ready</c> runs.</summary>
        public const string ReadyTag = "ready";

        /// <summary>The registry's snapshot is loaded and fresh.</summary>
        public const string Catalog = "tellma-catalog";

        /// <summary>The Data Protection key ring loads.</summary>
        public const string DataProtection = "tellma-data-protection";

        /// <summary>The identity server's discovery document is cached.</summary>
        public const string Authority = "tellma-authority";
    }

    /// <summary>Ready when the registry's snapshot is loaded and within its staleness bound.</summary>
    /// <param name="registry">The tenant registry.</param>
    internal sealed class CatalogHealthCheck(ITenantRegistry registry) : IHealthCheck
    {
        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(registry.IsAvailable
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The tenant snapshot is not loaded or is beyond its staleness bound."));
        }
    }

    /// <summary>Ready when a value round-trips through Data Protection, so the key ring loads.</summary>
    /// <param name="provider">The Data Protection provider.</param>
    internal sealed class DataProtectionHealthCheck(IDataProtectionProvider provider) : IHealthCheck
    {
        /// <summary>The purpose of the probe's protector, used for nothing else.</summary>
        private const string Purpose = "Tellma.Health.Ready";

        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                IDataProtector protector = provider.CreateProtector(Purpose);
                const string Probe = "ready";
                return Task.FromResult(string.Equals(protector.Unprotect(protector.Protect(Probe)), Probe, StringComparison.Ordinal)
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("A value did not round-trip through Data Protection."));
            }
            catch (CryptographicException exception)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("The Data Protection key ring could not be loaded.", exception));
            }
        }
    }

    /// <summary>Ready when the identity server's discovery document, and so its signing keys, are cached.</summary>
    /// <param name="bearer">The bearer scheme's options.</param>
    internal sealed class AuthorityHealthCheck(IOptionsMonitor<JwtBearerOptions> bearer) : IHealthCheck
    {
        /// <inheritdoc />
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            JwtBearerOptions options = bearer.Get(TellmaAuthentication.BearerScheme);
            if (options.Configuration is not null)
            {
                return HealthCheckResult.Healthy();
            }

            if (options.ConfigurationManager is null)
            {
                return HealthCheckResult.Unhealthy("The bearer scheme has no authority.");
            }

            try
            {
                // Served from the manager's cache once fetched; refetched on its schedule.
                _ = await options.ConfigurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
                return HealthCheckResult.Healthy();
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy("The identity server's discovery document could not be retrieved.", exception);
            }
        }
    }
}
