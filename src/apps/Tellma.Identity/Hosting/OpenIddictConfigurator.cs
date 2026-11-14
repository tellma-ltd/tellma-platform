// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Data;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Keys;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Hosting
{
    /// <summary>
    ///     Registers the OpenIddict protocol engine — every server and validation option lives
    ///     here, in one auditable place shared by both hosting shapes.
    /// </summary>
    internal static class OpenIddictConfigurator
    {
        /// <summary>Registers OpenIddict core, server, and validation.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options">The registration-time options snapshot.</param>
        public static void Configure(IServiceCollection services, TellmaIdentityOptions options)
        {
            string prefix = options.RoutePrefix;

            services.AddOpenIddict()
                .AddCore(core =>
                {
                    core.UseEntityFrameworkCore().UseDbContext<TellmaIdentityDbContext>();

                    // Token/authorization pruning — every issued token writes a metadata row, so
                    // the prune job is the single most important operational job.
                    core.UseQuartz();
                })
                .AddServer(server =>
                {
                    // Explicit issuer: deterministic `iss` for tokens and logout tokens in both
                    // hosting shapes.
                    server.SetIssuer(options.Issuer!);

                    // Relative endpoint URIs, prefixed with the reserved path base in in-proc
                    // mode so discovery lives at <issuer>/.well-known/openid-configuration.
                    server.SetAuthorizationEndpointUris(Prefixed(prefix, "connect/authorize"))
                          .SetTokenEndpointUris(Prefixed(prefix, "connect/token"))
                          .SetEndSessionEndpointUris(Prefixed(prefix, "connect/endsession"))
                          .SetDeviceAuthorizationEndpointUris(Prefixed(prefix, "connect/device"))
                          .SetEndUserVerificationEndpointUris(Prefixed(prefix, "connect/verify"))
                          .SetPushedAuthorizationEndpointUris(Prefixed(prefix, "connect/par"))
                          .SetIntrospectionEndpointUris(Prefixed(prefix, "connect/introspect"))
                          .SetRevocationEndpointUris(Prefixed(prefix, "connect/revoke"))
                          .SetUserInfoEndpointUris(Prefixed(prefix, "connect/userinfo"))
                          .SetConfigurationEndpointUris(Prefixed(prefix, ".well-known/openid-configuration"))
                          .SetJsonWebKeySetEndpointUris(Prefixed(prefix, ".well-known/jwks"));

                    // The grant catalog; implicit and ROPC stay disallowed.
                    server.AllowAuthorizationCodeFlow()
                          .AllowRefreshTokenFlow()
                          .AllowClientCredentialsFlow()
                          .AllowDeviceAuthorizationFlow()
                          .AllowTokenExchangeFlow();

                    // PKCE for every authorization-code client, public and confidential, and only
                    // the S256 challenge method — the plain method is a downgrade the OAuth
                    // Security BCP forbids, so it is removed from the advertised/accepted set.
                    server.RequireProofKeyForCodeExchange();
                    server.Configure(static serverOptions =>
                        serverOptions.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));

                    // The control-plane scope exists only in standalone mode (the operator surface
                    // is absent in-proc), so it is neither advertised nor grantable there.
                    string[] scopes = options.Mode == TellmaIdentityDeploymentMode.InProc
                        ?
                        [
                            Scopes.Email, Scopes.Profile, Scopes.OfflineAccess,
                            TellmaIdentityConstants.ApiScope, TellmaIdentityConstants.IdentityScope,
                        ]
                        :
                        [
                            Scopes.Email, Scopes.Profile, Scopes.OfflineAccess,
                            TellmaIdentityConstants.ApiScope, TellmaIdentityConstants.IdentityScope,
                            TellmaIdentityConstants.ControlPlaneScope,
                        ];
                    server.RegisterScopes(scopes);

                    server.SetAccessTokenLifetime(options.Lifetimes.AccessToken)
                          .SetIdentityTokenLifetime(options.Lifetimes.IdentityToken)
                          .SetRefreshTokenLifetime(options.Lifetimes.RefreshTokenIdle)
                          .SetRefreshTokenReuseLeeway(options.Lifetimes.RefreshTokenReuseLeeway);

                    // Signed-only JWT access tokens: any resource server validates them offline
                    // against the cached JWKS. Codes, refresh tokens, and device codes stay
                    // encrypted (the default).
                    server.DisableAccessTokenEncryption();

                    // `resource` parameters are validated by one engine check at every endpoint
                    // that accepts them (the resource grant handlers below), against the client's
                    // rsrc: permissions granted at provisioning time — plus one per-tenant path
                    // under a granted origin — or against the presented grant's own resources.
                    // OpenIddict's two built-in checks are therefore off: its global registry is
                    // startup-static and cannot know audiences of distributions provisioned at
                    // runtime, and its per-client permission check knows neither the per-tenant
                    // pattern nor the narrowing rule, and answers invalid_request rather than
                    // invalid_target.
                    server.DisableResourceValidation();
                    server.IgnoreResourcePermissions();

                    // Only asymmetric X.509 material is ever registered — a symmetric signing key
                    // would win selection and emit HS256 tokens that offline resource servers
                    // holding only the public JWKS could not validate.
                    foreach (X509Certificate2 certificate in CertificateSources.Load(options.Keys.Signing, CertificateUse.Signing))
                    {
                        AddSigningCertificate(server, certificate);
                    }

                    foreach (X509Certificate2 certificate in CertificateSources.Load(options.Keys.Encryption, CertificateUse.Encryption))
                    {
                        server.AddEncryptionCertificate(certificate);
                    }

                    // Mutual-TLS (RFC 8705) for confidential machine clients that opt into
                    // sender-constrained tokens. Off by default; the host negotiates the client
                    // certificate at the TLS layer when enabled.
                    if (options.MutualTls.Enabled)
                    {
                        if (options.MutualTls.AcceptSelfSignedClientCertificates)
                        {
                            server.EnableSelfSignedTlsClientAuthentication();
                        }

                        if (options.MutualTls.BindAccessTokens)
                        {
                            server.UseClientCertificateBoundAccessTokens();
                        }

                        if (options.MutualTls.BindRefreshTokens)
                        {
                            server.UseClientCertificateBoundRefreshTokens();
                        }
                    }

                    // Custom pipeline handlers: capture the presented token's subject during token
                    // validation (before the expiry/replay checks reject), then audit every
                    // token-endpoint and revocation-endpoint outcome — issuance, rejection,
                    // refresh replay, revocation — including failures the pass-through controller
                    // never sees.
                    server.AddEventHandler(Handlers.CaptureAuditSubjectHandler.Descriptor);
                    server.AddEventHandler(Handlers.AuditTokenResponseHandler.Descriptor);
                    server.AddEventHandler(Handlers.AuditRevocationResponseHandler.Descriptor);

                    // The principal kind (individual or service) is written into every access
                    // token from the grant type, in one place, after OpenIddict has built each
                    // token's principal and before any token is signed.
                    server.AddEventHandler(Handlers.TellmaKindClaimHandler.Descriptor);

                    // The resource grant check, one per endpoint that accepts `resource`, all
                    // asking the same validator: authorization, pushed authorization, device
                    // authorization and token. A device request's resources are carried into its
                    // codes, and a token request against a stored grant narrows only the access
                    // token's audience, never the refresh token's.
                    server.AddEventHandler(Handlers.ValidateAuthorizationResourcesHandler.Descriptor);
                    server.AddEventHandler(Handlers.ValidatePushedAuthorizationResourcesHandler.Descriptor);
                    server.AddEventHandler(Handlers.ValidateDeviceAuthorizationResourcesHandler.Descriptor);
                    server.AddEventHandler(Handlers.AttachDeviceAuthorizationResourcesHandler.Descriptor);
                    server.AddEventHandler(Handlers.ValidateTokenResourcesHandler.Descriptor);
                    server.AddEventHandler(Handlers.NarrowAccessTokenAudienceHandler.Descriptor);

                    // Pass-through: the engine's controllers shape every interactive protocol
                    // response while OpenIddict handles the wire format. The device-authorization
                    // endpoint itself has no pass-through (and needs none) — only the end-user
                    // verification page does.
                    server.UseAspNetCore(aspnetcore =>
                    {
                        aspnetcore.EnableAuthorizationEndpointPassthrough()
                                  .EnableTokenEndpointPassthrough()
                                  .EnableUserInfoEndpointPassthrough()
                                  .EnableEndSessionEndpointPassthrough()
                                  .EnableEndUserVerificationEndpointPassthrough()
                                  .EnableErrorPassthrough();

                        if (options.Development.AllowInsecureHttp)
                        {
                            aspnetcore.DisableTransportSecurityRequirement();
                        }
                    });
                })
                .AddValidation(validation =>
                {
                    // Same-process validation for the engine's own management APIs.
                    validation.UseLocalServer();
                    validation.UseAspNetCore();
                });
        }

        /// <summary>Prepends the in-proc route prefix to a relative endpoint path.</summary>
        private static string Prefixed(string prefix, string path)
        {
            return prefix.Length == 0 ? path : prefix + "/" + path;
        }

        /// <summary>
        ///     Registers one signing certificate. RSA certificates go through the standard X.509
        ///     path, which also gives them furthest-<c>NotAfter</c> priority during overlap
        ///     rotation. ECDSA certificates cannot be wrapped in an X.509 security key (the token
        ///     stack cannot infer or run ES256 through one), so they are registered as explicit
        ///     ECDSA credentials with the certificate thumbprint as the key id; deployments that
        ///     rely on overlap rotation should prefer RSA signing certificates.
        /// </summary>
        private static void AddSigningCertificate(OpenIddictServerBuilder server, X509Certificate2 certificate)
        {
            ECDsa? ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa is null)
            {
                server.AddSigningCertificate(certificate);
                return;
            }

            string algorithm = ecdsa.KeySize switch
            {
                256 => SecurityAlgorithms.EcdsaSha256,
                384 => SecurityAlgorithms.EcdsaSha384,
                521 => SecurityAlgorithms.EcdsaSha512,
                _ => throw new InvalidOperationException(
                    $"Unsupported ECDSA key size {ecdsa.KeySize} on signing certificate '{certificate.Thumbprint}'."),
            };

            ECDsaSecurityKey key = new(ecdsa) { KeyId = certificate.Thumbprint };
            server.AddSigningCredentials(new SigningCredentials(key, algorithm));
        }
    }
}
