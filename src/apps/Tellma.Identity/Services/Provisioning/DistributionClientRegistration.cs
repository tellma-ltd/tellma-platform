// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Provisioning
{
    /// <summary>
    ///     Everything that decides a distribution's two client registrations: the confidential BFF
    ///     client named by the slug and its <c>&lt;slug&gt;-svc</c> backend machine client. Onboarding
    ///     builds one with freshly generated secrets; a <c>Distribution</c> seed entry builds one
    ///     from configuration. Both converge through the same steps.
    /// </summary>
    /// <remarks>
    ///     A class rather than a record so that no generated <c>ToString</c> can print the secrets it
    ///     carries into a log.
    /// </remarks>
    public sealed class DistributionClientRegistration
    {
        /// <summary>The suffix that names a distribution's backend machine client after its slug.</summary>
        public const string ServiceClientIdSuffix = "-svc";

        /// <summary>Creates a registration.</summary>
        /// <param name="slug">The distribution slug (= the BFF client id).</param>
        /// <param name="origin">The distribution's browser origin; only its scheme, host and port are used.</param>
        /// <param name="backchannelLogoutUri">The BFF's back-channel logout endpoint, if it receives one.</param>
        /// <param name="clientSecret">The BFF client's secret.</param>
        /// <param name="serviceClientSecret">The backend machine client's secret.</param>
        /// <param name="allowTokenExchange">Grant the backend client the token-exchange grant.</param>
        public DistributionClientRegistration(
            string slug,
            Uri origin,
            Uri? backchannelLogoutUri,
            string clientSecret,
            string serviceClientSecret,
            bool allowTokenExchange)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slug);
            ArgumentNullException.ThrowIfNull(origin);
            ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
            ArgumentException.ThrowIfNullOrWhiteSpace(serviceClientSecret);
            if (!origin.IsAbsoluteUri)
            {
                throw new ArgumentException("A distribution origin must be an absolute URI.", nameof(origin));
            }

            Slug = slug;
            Origin = origin;
            BackchannelLogoutUri = backchannelLogoutUri;
            ClientSecret = clientSecret;
            ServiceClientSecret = serviceClientSecret;
            AllowTokenExchange = allowTokenExchange;
        }

        /// <summary>The distribution slug (= the BFF client id).</summary>
        public string Slug { get; }

        /// <summary>The backend machine client's id, <c>&lt;slug&gt;-svc</c>.</summary>
        public string ServiceClientId => Slug + ServiceClientIdSuffix;

        /// <summary>The distribution's browser origin.</summary>
        public Uri Origin { get; }

        /// <summary>The BFF's back-channel logout endpoint, if it receives one.</summary>
        public Uri? BackchannelLogoutUri { get; }

        /// <summary>The BFF client's secret.</summary>
        public string ClientSecret { get; }

        /// <summary>The backend machine client's secret.</summary>
        public string ServiceClientSecret { get; }

        /// <summary>Whether the backend client holds the token-exchange grant.</summary>
        public bool AllowTokenExchange { get; }
    }

    /// <summary>What converging one client registration did to the store.</summary>
    public enum ClientConvergence
    {
        /// <summary>The stored registration already matched; nothing was written.</summary>
        Unchanged = 0,

        /// <summary>No registration existed, and one was created.</summary>
        Created = 1,

        /// <summary>A registration existed and was rewritten to match (a rotated secret, a moved origin).</summary>
        Updated = 2,
    }

    /// <summary>What converging a distribution's registrations did to the store.</summary>
    /// <param name="Client">The BFF client's outcome.</param>
    /// <param name="ServiceClient">The backend machine client's outcome.</param>
    /// <param name="AudienceAdded">Whether the origin was newly added to the distribution API's audiences.</param>
    /// <param name="GrantsAdded">How many seeded platform clients were newly granted the origin.</param>
    public sealed record DistributionConvergence(
        ClientConvergence Client, ClientConvergence ServiceClient, bool AudienceAdded, int GrantsAdded)
    {
        /// <summary>Whether the convergence wrote anything at all.</summary>
        public bool Changed =>
            Client != ClientConvergence.Unchanged
            || ServiceClient != ClientConvergence.Unchanged
            || AudienceAdded
            || GrantsAdded > 0;
    }
}
