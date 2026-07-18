// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Provisioning
{
    /// <summary>The credentials produced by provisioning a distribution; secrets are returned exactly once.</summary>
    /// <param name="BffClientId">The BFF client id (the distribution slug).</param>
    /// <param name="BffClientSecret">The BFF's secret; written to the distribution's secret store by the caller.</param>
    /// <param name="ServiceClientId">The backend machine client id (<c>&lt;slug&gt;-svc</c>).</param>
    /// <param name="ServiceClientSecret">The backend machine client's secret.</param>
    public sealed record DistributionClientCredentials(
        string BffClientId, string BffClientSecret, string ServiceClientId, string ServiceClientSecret);

    /// <summary>A created service account's credentials; the secret is returned exactly once.</summary>
    /// <param name="ClientId">The generated client id.</param>
    /// <param name="ClientSecret">The generated secret.</param>
    public sealed record ServiceAccountCredentials(string ClientId, string ClientSecret);

    /// <summary>Service-account metadata; never includes the secret.</summary>
    /// <param name="ClientId">The client id.</param>
    /// <param name="DisplayName">Human-readable name.</param>
    /// <param name="CreatedUtc">When the account was created.</param>
    public sealed record ServiceAccountDetails(string ClientId, string? DisplayName, DateTimeOffset? CreatedUtc);

    /// <summary>
    ///     Registers OAuth clients at runtime — the platform's dynamic-client-registration
    ///     surface, built over the OpenIddict application manager. The server never tags a client
    ///     with a tenant; distributions record their own associations.
    /// </summary>
    public interface IClientProvisioningService
    {
        /// <summary>
        ///     Provisions (or re-provisions, rotating secrets) a distribution's confidential BFF
        ///     client and its backend machine client, registers the distribution's origin as a
        ///     requestable audience, and grants the origin to the seeded platform clients.
        /// </summary>
        /// <param name="slug">The distribution slug (= BFF client id).</param>
        /// <param name="origin">The distribution's browser origin.</param>
        /// <param name="backchannelLogoutUri">The BFF's back-channel logout endpoint.</param>
        /// <param name="allowTokenExchange">Grant the backend client token exchange (acting for users).</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The generated credentials; secrets are not retrievable afterwards.</returns>
        Task<DistributionClientCredentials> CreateDistributionAsync(
            string slug, Uri origin, Uri? backchannelLogoutUri, bool allowTokenExchange, CancellationToken cancellationToken);

        /// <summary>Creates a service account and returns its credentials once.</summary>
        /// <param name="displayName">Human-readable name recorded on the registration.</param>
        /// <param name="resources">The audiences the account may request.</param>
        /// <param name="createdByClientId">The calling client, for audit.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The generated credentials.</returns>
        Task<ServiceAccountCredentials> CreateServiceAccountAsync(
            string displayName, IReadOnlyCollection<string> resources, string? createdByClientId, CancellationToken cancellationToken);

        /// <summary>Reads a service account's metadata (never the secret).</summary>
        /// <param name="clientId">The service-account client id.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The details, or null when no such service account exists.</returns>
        Task<ServiceAccountDetails?> GetServiceAccountAsync(string clientId, CancellationToken cancellationToken);

        /// <summary>Deletes a service account (the lost-secret path is delete and recreate).</summary>
        /// <param name="clientId">The service-account client id.</param>
        /// <param name="deletedByClientId">The calling client, for audit.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>Whether a service account was found and deleted.</returns>
        Task<bool> DeleteServiceAccountAsync(string clientId, string? deletedByClientId, CancellationToken cancellationToken);

        /// <summary>Regenerates a service account's secret (operator path) and returns it once.</summary>
        /// <param name="clientId">The service-account client id.</param>
        /// <param name="requestedByClientId">The calling client, for audit.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The new secret, or null when no such service account exists.</returns>
        Task<string?> RegenerateServiceAccountSecretAsync(
            string clientId, string? requestedByClientId, CancellationToken cancellationToken);
    }
}
