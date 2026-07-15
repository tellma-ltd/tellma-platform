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
        /// <param name="issuerOrigin">The authority's own origin (the management API audience).</param>
        /// <param name="secret">The generated client secret.</param>
        /// <param name="allowTokenExchange">Grant the token-exchange grant type.</param>
        /// <returns>The descriptor.</returns>
        public static OpenIddictApplicationDescriptor DistributionService(
            string slug, Uri origin, string issuerOrigin, string secret, bool allowTokenExchange)
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
                    Permissions.Prefixes.Resource + issuerOrigin,
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
        ///     named resource permissions and nothing else.
        /// </summary>
        /// <param name="clientId">The generated client id.</param>
        /// <param name="displayName">Human-readable name.</param>
        /// <param name="secret">The generated client secret.</param>
        /// <param name="resources">The audiences the account may request.</param>
        /// <param name="createdUtc">Creation timestamp recorded on the registration.</param>
        /// <returns>The descriptor.</returns>
        public static OpenIddictApplicationDescriptor ServiceAccount(
            string clientId, string displayName, string secret, IEnumerable<string> resources, DateTimeOffset createdUtc)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
            ArgumentNullException.ThrowIfNull(resources);

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
            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.CreatedUtc, createdUtc.ToString("O"));

            return descriptor;
        }

        /// <summary>Builds the descriptor for one seeded platform client.</summary>
        /// <param name="options">The engine options (issuer origin, mode).</param>
        /// <param name="seed">The seed definition.</param>
        /// <returns>The descriptor.</returns>
        public static OpenIddictApplicationDescriptor SeededClient(
            TellmaIdentityOptions options, TellmaIdentitySeedClientOptions seed)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(seed);
            ArgumentException.ThrowIfNullOrWhiteSpace(seed.ClientId);

            string issuerOrigin = options.Issuer!.GetLeftPart(UriPartial.Authority);
            OpenIddictApplicationDescriptor descriptor = seed.Kind switch
            {
                TellmaIdentitySeedClientKind.Cli => PublicNativeClient(seed, includeIdentityScope: true),
                TellmaIdentitySeedClientKind.Native => PublicNativeClient(seed, includeIdentityScope: false),
                TellmaIdentitySeedClientKind.ControlPlane => ControlPlane(seed, issuerOrigin),
                _ => throw new InvalidOperationException($"Unknown seed client kind for '{seed.ClientId}'."),
            };

            foreach (string resource in seed.Resources)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Resource + resource);
            }

            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.Platform, "true");
            TellmaClientProperties.Set(descriptor.Properties, TellmaClientProperties.FirstParty, "true");

            return descriptor;
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

            return descriptor;
        }

        /// <summary>The control plane: a confidential client-credentials caller.</summary>
        private static OpenIddictApplicationDescriptor ControlPlane(
            TellmaIdentitySeedClientOptions seed, string issuerOrigin)
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
                    Permissions.Prefixes.Resource + issuerOrigin,
                },
                };
        }
    }
}
