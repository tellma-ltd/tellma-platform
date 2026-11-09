// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using Tellma.Identity.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.Provisioning
{
    /// <summary>
    ///     Builds least-privilege <see cref="OpenIddictApplicationDescriptor" />s for every client
    ///     archetype the platform registers. All permission decisions live here, in one auditable
    ///     place.
    /// </summary>
    public static class ClientDescriptorFactory
    {
        /// <summary>
        ///     A distribution's confidential BFF client: Authorization Code + PKCE over exact
        ///     redirect URIs, refresh tokens held server-side, parameters always pushed (PAR), and
        ///     consent implicit (first-party).
        /// </summary>
        /// <param name="slug">The distribution slug (= client id).</param>
        /// <param name="origin">The distribution's browser origin.</param>
        /// <param name="backchannelLogoutUri">The BFF's back-channel logout endpoint.</param>
        /// <param name="secret">The generated client secret.</param>
        /// <returns>The descriptor.</returns>
        public static OpenIddictApplicationDescriptor Distribution(
            string slug, Uri origin, Uri? backchannelLogoutUri, string secret)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slug);
            ArgumentNullException.ThrowIfNull(origin);

            string originValue = origin.GetLeftPart(UriPartial.Authority);
            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = slug,
                ClientSecret = secret,
                DisplayName = slug,
                ClientType = ClientTypes.Confidential,
                ApplicationType = ApplicationTypes.Web,
                ConsentType = ConsentTypes.Implicit,
                RedirectUris = { new Uri(originValue + "/signin-oidc") },
                PostLogoutRedirectUris = { new Uri(originValue + "/signout-callback-oidc") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.EndSession,
                    Permissions.Endpoints.Revocation,
                    Permissions.Endpoints.PushedAuthorization,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.ApiScope,
                    Permissions.Prefixes.Resource + originValue,
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange,
                    Requirements.Features.PushedAuthorizationRequests,
                },
            };

            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.Origin, originValue);
            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.FirstParty, "true");
            TellmaClientProperties.Set(
                descriptor.Properties, TellmaClientProperties.BackchannelLogoutUri, backchannelLogoutUri?.AbsoluteUri);

            return descriptor;
        }

        /// <summary>
        ///     A distribution backend's machine client (<c>&lt;slug&gt;-svc</c>): client
        ///     credentials for the server management API, plus token exchange when the backend
        ///     acts for users.
        /// </summary>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="origin">The distribution's browser origin.</param>
        /// <param name="secret">The generated client secret.</param>
        /// <param name="allowTokenExchange">Grant the token-exchange grant type.</param>
        /// <returns>The descriptor.</returns>
        public static OpenIddictApplicationDescriptor DistributionService(
            string slug, Uri origin, string secret, bool allowTokenExchange)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slug);
            ArgumentNullException.ThrowIfNull(origin);

            string originValue = origin.GetLeftPart(UriPartial.Authority);
            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = slug + "-svc",
                ClientSecret = secret,
                DisplayName = slug + " backend",
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Implicit,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.ApiScope,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.IdentityScope,
                    Permissions.Prefixes.Resource + originValue,
                    Permissions.Prefixes.Resource + TellmaIdentityConstants.IdentityAudience,
                },
            };

            if (allowTokenExchange)
            {
                descriptor.Permissions.Add(Permissions.GrantTypes.TokenExchange);
            }

            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.Origin, originValue);
            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.FirstParty, "true");

            return descriptor;
        }

        /// <summary>
        ///     A tenant service account: a confidential client-credentials caller with explicitly
        ///     named resource permissions and nothing else. The owning distribution's origin is
        ///     recorded so reads and deletes can be scoped to the distribution that created it.
        /// </summary>
        /// <param name="clientId">The generated client id.</param>
        /// <param name="displayName">Human-readable name.</param>
        /// <param name="secret">The generated client secret.</param>
        /// <param name="resources">The audiences the account may request.</param>
        /// <param name="ownerOrigin">The creating distribution's origin (the ownership marker).</param>
        /// <param name="createdUtc">Creation timestamp recorded on the registration.</param>
        /// <returns>The descriptor.</returns>
        public static OpenIddictApplicationDescriptor ServiceAccount(
            string clientId,
            string displayName,
            string secret,
            IEnumerable<string> resources,
            string ownerOrigin,
            DateTimeOffset createdUtc)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
            ArgumentNullException.ThrowIfNull(resources);
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerOrigin);

            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = clientId,
                ClientSecret = secret,
                DisplayName = displayName,
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Implicit,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.ApiScope,
                },
            };

            foreach (string resource in resources)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Resource + resource);
            }

            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.ServiceAccount, "true");
            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.Origin, ownerOrigin);
            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.CreatedUtc, createdUtc.ToString("O"));

            return descriptor;
        }

        /// <summary>Builds the descriptor for one seeded platform client.</summary>
        /// <param name="options">The engine options.</param>
        /// <param name="seed">The seed definition.</param>
        /// <returns>The descriptor.</returns>
        public static OpenIddictApplicationDescriptor SeededClient(
            TellmaIdentityOptions options, TellmaIdentitySeedClientOptions seed)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(seed);
            ArgumentException.ThrowIfNullOrWhiteSpace(seed.ClientId);

            OpenIddictApplicationDescriptor descriptor = seed.Kind switch
            {
                TellmaIdentitySeedClientKind.Cli => PublicNativeClient(seed, includeIdentityScope: true),
                TellmaIdentitySeedClientKind.Native => PublicNativeClient(seed, includeIdentityScope: false),
                TellmaIdentitySeedClientKind.ControlPlane => ControlPlane(seed),
                TellmaIdentitySeedClientKind.Distribution => throw new ArgumentException(
                    $"Seed client '{seed.ClientId}' is a Distribution entry, which registers two clients; build it with SeededDistribution.",
                    nameof(seed)),
                _ => throw new InvalidOperationException($"Unknown seed client kind for '{seed.ClientId}'."),
            };

            foreach (string resource in seed.Resources)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Resource + resource);
            }

            // A third-party client is never trusted the way the platform's own are: its consent is
            // explicit whatever RequireConsent says, and it carries no first-party marker, so no
            // first-party treatment reaches it. Startup validation accepts the flag on Native
            // entries only; it is refused here on any other kind as well, so a descriptor can never
            // be built from an entry that slipped past it.
            if (seed.ThirdParty && seed.Kind != TellmaIdentitySeedClientKind.Native)
            {
                throw new ArgumentException(
                    $"Seed client '{seed.ClientId}' sets ThirdParty, which only a Native entry accepts.", nameof(seed));
            }

            if (seed.RequireConsent || seed.ThirdParty)
            {
                descriptor.ConsentType = ConsentTypes.Explicit;
            }

            if (!seed.ThirdParty)
            {
                TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.FirstParty, "true");
            }

            return descriptor;
        }

        /// <summary>
        ///     The registration a <see cref="TellmaIdentitySeedClientKind.Distribution" /> seed entry
        ///     converges on: onboarding's two clients for the entry's slug and origin, with the
        ///     entry's secrets in place of generated ones and token exchange off. The back-channel
        ///     logout endpoint defaults to the BFF's own under the origin.
        /// </summary>
        /// <param name="seed">A validated Distribution seed entry.</param>
        /// <returns>The registration.</returns>
        /// <exception cref="InvalidOperationException">The entry has no secret for one of its clients.</exception>
        public static DistributionClientRegistration SeededDistribution(TellmaIdentitySeedClientOptions seed)
        {
            ArgumentNullException.ThrowIfNull(seed);
            ArgumentException.ThrowIfNullOrWhiteSpace(seed.ClientId);
            ArgumentException.ThrowIfNullOrWhiteSpace(seed.Origin);
            if (seed.Kind != TellmaIdentitySeedClientKind.Distribution)
            {
                throw new ArgumentException($"Seed client '{seed.ClientId}' is not a Distribution entry.", nameof(seed));
            }

            // The seeder never invents a secret: in Development a host without one in its
            // configuration supplies a per-boot secret before the entry reaches the engine, and
            // outside Development startup validation has already refused the entry.
            if (string.IsNullOrWhiteSpace(seed.ClientSecret) || string.IsNullOrWhiteSpace(seed.ServiceClientSecret))
            {
                throw new InvalidOperationException(
                    $"Seeded distribution '{seed.ClientId}' requires both ClientSecret and ServiceClientSecret: its "
                        + "clients are confidential, and the seeder never generates a secret.");
            }

            Uri origin = new(seed.Origin, UriKind.Absolute);
            Uri backchannelLogoutUri = string.IsNullOrWhiteSpace(seed.BackchannelLogoutUri)
                ? new Uri(origin.GetLeftPart(UriPartial.Authority) + TellmaIdentitySeedClientOptions.DefaultBackchannelLogoutPath)
                : new Uri(seed.BackchannelLogoutUri, UriKind.Absolute);

            return new DistributionClientRegistration(
                seed.ClientId, origin, backchannelLogoutUri, seed.ClientSecret, seed.ServiceClientSecret, allowTokenExchange: false);
        }

        /// <summary>
        ///     A public native client (CLI / native app): Authorization Code + PKCE via the system
        ///     browser and the Device Authorization Grant. The native application type relaxes
        ///     loopback redirect matching, so a portless <c>http://127.0.0.1/...</c> registration
        ///     accepts any ephemeral port at runtime.
        /// </summary>
        private static OpenIddictApplicationDescriptor PublicNativeClient(
            TellmaIdentitySeedClientOptions seed, bool includeIdentityScope)
        {
            OpenIddictApplicationDescriptor descriptor = new()
            {
                ClientId = seed.ClientId,
                DisplayName = seed.DisplayName ?? seed.ClientId,
                ClientType = ClientTypes.Public,
                ApplicationType = ApplicationTypes.Native,
                ConsentType = ConsentTypes.Implicit,
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.DeviceAuthorization,
                    Permissions.Endpoints.EndSession,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.GrantTypes.DeviceCode,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.ApiScope,
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange,
                },
            };

            if (includeIdentityScope)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + TellmaIdentityConstants.IdentityScope);
            }

            foreach (string redirectUri in seed.RedirectUris)
            {
                descriptor.RedirectUris.Add(new Uri(redirectUri, UriKind.Absolute));
            }

            // CLI and native apps legitimately name distribution APIs; the control plane never does.
            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.CallsDistributionApis, "true");

            return descriptor;
        }

        /// <summary>
        ///     The control plane: a confidential client-credentials caller. It stores no
        ///     distribution grant: its resource permissions are the two platform audiences of the
        ///     identity server's own APIs, and a distribution's origin is checked against the
        ///     distribution-origin list when a request names it.
        /// </summary>
        private static OpenIddictApplicationDescriptor ControlPlane(
            TellmaIdentitySeedClientOptions seed)
        {
            return string.IsNullOrWhiteSpace(seed.ClientSecret)
                ? throw new InvalidOperationException(
                    $"Seeded control-plane client '{seed.ClientId}' requires a ClientSecret sourced from the deployment's secret store.")
                : new OpenIddictApplicationDescriptor
                {
                    ClientId = seed.ClientId,
                    ClientSecret = seed.ClientSecret,
                    DisplayName = seed.DisplayName ?? seed.ClientId,
                    ClientType = ClientTypes.Confidential,
                    ConsentType = ConsentTypes.Implicit,
                    Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.ControlPlaneScope,
                    Permissions.Prefixes.Scope + TellmaIdentityConstants.IdentityScope,
                    Permissions.Prefixes.Resource + TellmaIdentityConstants.ControlPlaneAudience,
                    Permissions.Prefixes.Resource + TellmaIdentityConstants.IdentityAudience,
                },
                };
        }
    }
}
