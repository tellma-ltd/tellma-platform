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

            // Onboarding always rotates: fresh secrets make every run a create or an update.
            DistributionClientRegistration registration = new(
                slug, origin, backchannelLogoutUri, GenerateSecret(), GenerateSecret(), allowTokenExchange);
            await ConvergeDistributionAsync(registration, cancellationToken);

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.ClientCreated,
                    ClientId = slug,
                    DetailsJson = JsonSerializer.Serialize(
                        new { origin = origin.GetLeftPart(UriPartial.Authority), kind = "distribution" }),
                },
                cancellationToken);

            return new DistributionClientCredentials(
                slug, registration.ClientSecret, registration.ServiceClientId, registration.ServiceClientSecret);
        }

        /// <inheritdoc />
        public async Task<DistributionConvergence> ConvergeDistributionAsync(
            DistributionClientRegistration registration, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(registration);

            string issuerOrigin = options.Value.Issuer!.GetLeftPart(UriPartial.Authority);
            string originValue = registration.Origin.GetLeftPart(UriPartial.Authority);

            ClientConvergence client = await ConvergeAsync(
                ClientDescriptorFactory.Distribution(
                    registration.Slug, registration.Origin, registration.BackchannelLogoutUri, registration.ClientSecret),
                cancellationToken);
            ClientConvergence serviceClient = await ConvergeAsync(
                ClientDescriptorFactory.DistributionService(
                    registration.Slug,
                    registration.Origin,
                    issuerOrigin,
                    registration.ServiceClientSecret,
                    registration.AllowTokenExchange),
                cancellationToken);

            // The origin becomes a requestable audience: recorded on the tellma_api scope entity
            // (the audience registry) and granted to the seeded platform clients so the CLI and
            // native apps may name it via the `resource` parameter.
            bool audienceAdded = await AppendScopeResourceAsync(TellmaIdentityConstants.ApiScope, originValue, cancellationToken);
            int grantsAdded = await GrantResourceToPlatformClientsAsync(originValue, cancellationToken);

            return new DistributionConvergence(client, serviceClient, audienceAdded, grantsAdded);
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

        /// <summary>
        ///     Creates a client registration, or rewrites an existing one with the same client id to
        ///     match the descriptor. A registration that already matches — its secret included — is
        ///     left untouched, so no write happens and no fresh hash is stored.
        /// </summary>
        /// <param name="descriptor">The desired registration, with its secret in plain text.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>What happened to the stored registration.</returns>
        private async Task<ClientConvergence> ConvergeAsync(
            OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken)
        {
            object? existing = await applicationManager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
            if (existing is null)
            {
                await applicationManager.CreateAsync(descriptor, cancellationToken);
                return ClientConvergence.Created;
            }

            OpenIddictApplicationDescriptor current = new();
            await applicationManager.PopulateAsync(current, existing, cancellationToken);

            // The stored secret is a salted hash, so equality is decided by validating the plain
            // secret against it. A matching secret is carried as the stored hash: the manager
            // re-hashes only a secret that differs from what it holds, so an update made for
            // another reason keeps the hash, and a no-op keeps everything.
            bool secretMatches = descriptor.ClientSecret is null
                ? current.ClientSecret is null
                : await applicationManager.ValidateClientSecretAsync(existing, descriptor.ClientSecret, cancellationToken);
            if (secretMatches)
            {
                descriptor.ClientSecret = current.ClientSecret;
                if (HaveSameRegistration(current, descriptor))
                {
                    return ClientConvergence.Unchanged;
                }
            }

            await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
            return ClientConvergence.Updated;
        }

        /// <summary>
        ///     Whether two descriptors describe the same registration in every member the platform
        ///     sets, compared as sets where order carries no meaning. Secrets are compared as stored.
        /// </summary>
        /// <param name="left">One descriptor.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when writing one over the other would change nothing.</returns>
        internal static bool HaveSameRegistration(OpenIddictApplicationDescriptor left, OpenIddictApplicationDescriptor right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);

            return string.Equals(left.ClientId, right.ClientId, StringComparison.Ordinal)
                && string.Equals(left.ClientSecret, right.ClientSecret, StringComparison.Ordinal)
                && string.Equals(left.ClientType, right.ClientType, StringComparison.Ordinal)
                && string.Equals(left.ApplicationType, right.ApplicationType, StringComparison.Ordinal)
                && string.Equals(left.ConsentType, right.ConsentType, StringComparison.Ordinal)
                && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
                && left.DisplayNames.Count == right.DisplayNames.Count
                && left.DisplayNames.All(pair => right.DisplayNames.TryGetValue(pair.Key, out string? name)
                    && string.Equals(pair.Value, name, StringComparison.Ordinal))
                && left.JsonWebKeySet is null && right.JsonWebKeySet is null
                && left.Permissions.SetEquals(right.Permissions)
                && left.Requirements.SetEquals(right.Requirements)
                && left.RedirectUris.SetEquals(right.RedirectUris)
                && left.PostLogoutRedirectUris.SetEquals(right.PostLogoutRedirectUris)
                && left.Properties.Count == right.Properties.Count
                && left.Properties.All(pair => right.Properties.TryGetValue(pair.Key, out JsonElement value)
                    && JsonElement.DeepEquals(pair.Value, value))
                && left.Settings.Count == right.Settings.Count
                && left.Settings.All(pair => right.Settings.TryGetValue(pair.Key, out string? value)
                    && string.Equals(pair.Value, value, StringComparison.Ordinal));
        }

        /// <summary>Appends one resource to a scope entity's resource registry.</summary>
        /// <returns>Whether the resource was new to the scope.</returns>
        private async Task<bool> AppendScopeResourceAsync(string scopeName, string resource, CancellationToken cancellationToken)
        {
            object? scope = await scopeManager.FindByNameAsync(scopeName, cancellationToken);
            if (scope is null)
            {
                OpenIddictScopeDescriptor created = new() { Name = scopeName };
                created.Resources.Add(resource);
                await scopeManager.CreateAsync(created, cancellationToken);
                return true;
            }

            OpenIddictScopeDescriptor descriptor = new();
            await scopeManager.PopulateAsync(descriptor, scope, cancellationToken);
            if (!descriptor.Resources.Add(resource))
            {
                return false;
            }

            await scopeManager.UpdateAsync(scope, descriptor, cancellationToken);
            return true;
        }

        /// <summary>
        ///     Grants a new distribution's audience to the platform clients that may name
        ///     distribution APIs (CLI, native apps). The control plane is deliberately excluded: its
        ///     only audience is the control-plane surface, so a compromised control-plane secret
        ///     cannot mint tokens for a distribution API.
        /// </summary>
        /// <returns>How many clients were newly granted the resource.</returns>
        private async Task<int> GrantResourceToPlatformClientsAsync(string resource, CancellationToken cancellationToken)
        {
            string permission = Permissions.Prefixes.Resource + resource;

            // Read the whole list before writing any of it. The store streams this query over the
            // request's single connection, and multiple active result sets are not enabled, so an
            // update issued while that reader is still open fails outright. Only a client carrying
            // the marker is written, so a registry holding none never reached the write and never
            // showed the fault.
            List<object> applications = [];
            await foreach (object application in applicationManager.ListAsync(cancellationToken: cancellationToken))
            {
                applications.Add(application);
            }

            int granted = 0;
            foreach (object application in applications)
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
                    granted++;
                }
            }

            return granted;
        }
    }
}
