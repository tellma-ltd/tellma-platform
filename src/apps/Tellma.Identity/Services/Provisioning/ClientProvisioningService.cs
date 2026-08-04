// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using System.Buffers.Text;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.Provisioning
{
    /// <summary>
    ///     The runtime client-registration surface over <see cref="IOpenIddictApplicationManager" />.
    ///     Secrets are generated from a CSPRNG, hashed at rest by OpenIddict, and returned to the
    ///     caller exactly once.
    /// </summary>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="scopeManager">The OpenIddict scope manager.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class ClientProvisioningService(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        IOptions<TellmaIdentityOptions> options,
        IAuditLogger auditLogger,
        TimeProvider timeProvider) : IClientProvisioningService
    {
        /// <summary>Prefix distinguishing runtime-created service accounts from platform clients.</summary>
        public const string ServiceAccountClientIdPrefix = "svc_";

        /// <inheritdoc />
        public async Task<DistributionClientCredentials> CreateDistributionAsync(
            string slug, Uri origin, Uri? backchannelLogoutUri, bool allowTokenExchange, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slug);
            ArgumentNullException.ThrowIfNull(origin);

            string issuerOrigin = options.Value.Issuer!.GetLeftPart(UriPartial.Authority);
            string originValue = origin.GetLeftPart(UriPartial.Authority);

            string bffSecret = GenerateSecret();
            string serviceSecret = GenerateSecret();

            await UpsertAsync(
                ClientDescriptorFactory.Distribution(slug, origin, backchannelLogoutUri, bffSecret), cancellationToken);
            await UpsertAsync(
                ClientDescriptorFactory.DistributionService(slug, origin, issuerOrigin, serviceSecret, allowTokenExchange),
                cancellationToken);

            // The origin becomes a requestable audience: recorded on the tellma_api scope entity
            // (the audience registry) and granted to the seeded platform clients so the CLI and
            // native apps may name it via the `resource` parameter.
            await AppendScopeResourceAsync(TellmaIdentityConstants.ApiScope, originValue, cancellationToken);
            await GrantResourceToPlatformClientsAsync(originValue, cancellationToken);

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.ClientCreated,
                    ClientId = slug,
                    DetailsJson = JsonSerializer.Serialize(new { origin = originValue, kind = "distribution" }),
                },
                cancellationToken);

            return new DistributionClientCredentials(slug, bffSecret, slug + "-svc", serviceSecret);
        }

        /// <inheritdoc />
        public async Task<ServiceAccountCredentials> CreateServiceAccountAsync(
            string displayName, IReadOnlyCollection<string> resources, string? createdByClientId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            ArgumentNullException.ThrowIfNull(resources);

            // Audience least-privilege: a service account may only name audiences its creating
            // client owns. Resolving the caller's own origin and rejecting anything else stops one
            // distribution's backend from minting tokens whose `aud` is a foreign distribution.
            string callerOrigin = await ResolveClientOriginAsync(createdByClientId, cancellationToken)
                ?? throw new ProvisioningValidationException(
                    "The calling client has no distribution origin, so it cannot create service accounts.");

            foreach (string resource in resources)
            {
                if (!string.Equals(resource, callerOrigin, StringComparison.Ordinal))
                {
                    throw new ProvisioningValidationException(
                        $"A service account may only be granted its own distribution's audience ('{callerOrigin}').");
                }
            }

            string clientId = ServiceAccountClientIdPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
            string secret = GenerateSecret();

            await applicationManager.CreateAsync(
                ClientDescriptorFactory.ServiceAccount(
                    clientId, displayName, secret, [callerOrigin], callerOrigin, timeProvider.GetUtcNow()),
                cancellationToken);

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.ServiceAccountCreated,
                    ClientId = clientId,
                    DetailsJson = JsonSerializer.Serialize(new { displayName, createdBy = createdByClientId }),
                },
                cancellationToken);

            return new ServiceAccountCredentials(clientId, secret);
        }

        /// <inheritdoc />
        public async Task<ServiceAccountDetails?> GetServiceAccountAsync(
            string clientId, ClaimsPrincipal caller, CancellationToken cancellationToken)
        {
            (object? application, ImmutableDictionary<string, JsonElement> properties) =
                await FindVisibleServiceAccountAsync(clientId, caller, cancellationToken);
            if (application is null)
            {
                return null;
            }

            string? created = TellmaClientProperties.Get(properties, TellmaClientProperties.CreatedUtc);

            return new ServiceAccountDetails(
                clientId,
                await applicationManager.GetDisplayNameAsync(application, cancellationToken),
                DateTimeOffset.TryParse(created, out DateTimeOffset createdUtc) ? createdUtc : null);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteServiceAccountAsync(
            string clientId, ClaimsPrincipal caller, CancellationToken cancellationToken)
        {
            (object? application, _) = await FindVisibleServiceAccountAsync(clientId, caller, cancellationToken);
            if (application is null)
            {
                return false;
            }

            await applicationManager.DeleteAsync(application, cancellationToken);

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.ServiceAccountDeleted,
                    ClientId = clientId,
                    DetailsJson = JsonSerializer.Serialize(new { deletedBy = CallerClientId(caller) }),
                },
                cancellationToken);

            return true;
        }

        /// <summary>The calling client's id, from the validated token.</summary>
        private static string? CallerClientId(ClaimsPrincipal caller)
        {
            return caller.GetClaim(Claims.ClientId) ?? caller.GetClaim(Claims.Subject);
        }

        /// <summary>Resolves a client's distribution origin from its stored properties.</summary>
        private async Task<string?> ResolveClientOriginAsync(string? clientId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(clientId)
                || await applicationManager.FindByClientIdAsync(clientId, cancellationToken) is not { } client)
            {
                return null;
            }

            ImmutableDictionary<string, JsonElement> properties =
                await applicationManager.GetPropertiesAsync(client, cancellationToken);
            return TellmaClientProperties.Get(properties, TellmaClientProperties.Origin);
        }

        /// <summary>Generates a 256-bit URL-safe client secret.</summary>
        internal static string GenerateSecret()
        {
            return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        }

        /// <summary>
        ///     Finds an application only if it is a runtime-created service account the caller may
        ///     see: one its own distribution owns, or — for a control-plane caller — any of them.
        ///     Its stored properties come back with it, so a caller that needs them (the read path
        ///     wants the creation timestamp) does not pay for a second round trip to re-read what
        ///     the visibility check already loaded.
        /// </summary>
        /// <param name="clientId">The service-account client id.</param>
        /// <param name="caller">The validated principal of the calling client.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The application and its properties, or nulls when the caller may not see it.</returns>
        private async Task<(object? Application, ImmutableDictionary<string, JsonElement> Properties)>
            FindVisibleServiceAccountAsync(string clientId, ClaimsPrincipal caller, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(caller);

            (object?, ImmutableDictionary<string, JsonElement>) invisible = (null, []);
            if (string.IsNullOrWhiteSpace(clientId) || !clientId.StartsWith(ServiceAccountClientIdPrefix, StringComparison.Ordinal))
            {
                return invisible;
            }

            object? application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
            if (application is null)
            {
                return invisible;
            }

            // The API must never read or delete arbitrary platform clients, whoever asks.
            ImmutableDictionary<string, JsonElement> properties =
                await applicationManager.GetPropertiesAsync(application, cancellationToken);
            if (!TellmaClientProperties.IsSet(properties, TellmaClientProperties.ServiceAccount))
            {
                return invisible;
            }

            // The control plane administers every service account — including one whose owning
            // distribution is gone, and one created before ownership was recorded, which no
            // distribution can claim. The scope is read from the token the server itself issued
            // and validated, so this cannot be asserted by a caller.
            if (ApiPolicies.HasScope(caller, TellmaIdentityConstants.ControlPlaneScope))
            {
                return (application, properties);
            }

            // One distribution must never read or delete another's: the account's recorded owner
            // origin must match the caller's.
            string? ownerOrigin = TellmaClientProperties.Get(properties, TellmaClientProperties.Origin);
            string? callerOrigin = await ResolveClientOriginAsync(CallerClientId(caller), cancellationToken);
            return ownerOrigin is not null
                && callerOrigin is not null
                && string.Equals(ownerOrigin, callerOrigin, StringComparison.Ordinal)
                    ? (application, properties)
                    : invisible;
        }

        /// <summary>Creates or replaces a client registration by client id.</summary>
        private async Task UpsertAsync(OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken)
        {
            object? existing = await applicationManager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
            if (existing is null)
            {
                await applicationManager.CreateAsync(descriptor, cancellationToken);
            }
            else
            {
                await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
            }
        }

        /// <summary>Appends one resource to a scope entity's resource registry.</summary>
        private async Task AppendScopeResourceAsync(string scopeName, string resource, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(scopeName, cancellationToken);
            if (scope is null)
            {
                OpenIddictScopeDescriptor created = new() { Name = scopeName };
                created.Resources.Add(resource);
                await scopeManager.CreateAsync(created, cancellationToken);
                return;
            }

            OpenIddictScopeDescriptor descriptor = new();
            await scopeManager.PopulateAsync(descriptor, scope, cancellationToken);
            if (descriptor.Resources.Add(resource))
            {
                await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
            }
        }

        /// <summary>
        ///     Grants a new distribution's audience to the platform clients that may name
        ///     distribution APIs (CLI, native apps). The control plane is deliberately excluded: its
        ///     only audience is the control-plane surface, so a compromised control-plane secret
        ///     cannot mint tokens for a distribution API.
        /// </summary>
        private async Task GrantResourceToPlatformClientsAsync(string resource, CancellationToken cancellationToken)
        {
            string permission = Permissions.Prefixes.Resource + resource;
            await foreach (object application in applicationManager.ListAsync(cancellationToken: cancellationToken))
            {
                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                if (!TellmaClientProperties.IsSet(properties, TellmaClientProperties.CallsDistributionApis))
                {
                    continue;
                }

                OpenIddictApplicationDescriptor descriptor = new();
                await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
                if (descriptor.Permissions.Add(permission))
                {
                    await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
                }
            }
        }
    }
}
