// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Tellma.Core.AspNetCore.Bff;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Sessions;

namespace Tellma.Core.AspNetCore.Authentication
{
    /// <summary>
    ///     Registers the host's authentication schemes and authorization policies: the session cookie
    ///     and the sign-in that starts it, and the bearer scheme. No scheme is the default: every policy
    ///     names its scheme, so a cookie never authenticates a bearer surface and a bearer never
    ///     authenticates the web surface, and an endpoint with no policy of its own falls to the
    ///     session-cookie fallback.
    /// </summary>
    internal static class TellmaAuthenticationComposition
    {
        /// <summary>The page a sign-out ends on.</summary>
        public const string SignedOutPath = "/signed-out";

        /// <summary>The bearer handler's tolerance for clock drift, against ten-minute access tokens.</summary>
        internal static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);

        /// <summary>The scopes the sign-in requests.</summary>
        internal static readonly string[] SignInScopes = ["openid", "profile", "email", "offline_access", TellmaClaimTypes.ApiScope];

        /// <summary>Registers the schemes, the policies and the bearer requirements' handler.</summary>
        /// <param name="services">The services.</param>
        /// <param name="slug">The distribution's slug: its sign-in client's id at the identity server.</param>
        public static void Register(IServiceCollection services, string slug)
        {
            // --- The schemes ---
            _ = services
                .AddAuthentication()
                .AddCookie(TellmaAuthentication.SessionScheme)
                .AddOpenIdConnect(TellmaAuthentication.OidcScheme, static _ => { })
                .AddJwtBearer(TellmaAuthentication.BearerScheme);

            _ = services.AddOptions<CookieAuthenticationOptions>(TellmaAuthentication.SessionScheme)
                .Configure<IOptions<TellmaSessionOptions>, ICatalogSessionStore, SessionCookieSecurity>(ConfigureSession);
            _ = services.AddOptions<OpenIdConnectOptions>(TellmaAuthentication.OidcScheme)
                .Configure<IConfiguration, IOptions<IdentityHostOptions>, IHostEnvironment, IdentityClientSecrets, SessionCookieSecurity>(
                    (options, configuration, identity, environment, secrets, cookies) =>
                        ConfigureSignIn(options, slug, configuration, identity.Value, environment, secrets, cookies));
            _ = services.AddOptions<JwtBearerOptions>(TellmaAuthentication.BearerScheme)
                .Configure<IConfiguration, IOptions<IdentityHostOptions>, IHostEnvironment>(ConfigureBearer);

            // --- The policies: each names its scheme ---
            // The default policy stays the framework's, naming no scheme, so an in-proc identity
            // engine's [Authorize] endpoints keep the engine's own default scheme; the endpoint audit
            // refuses any other endpoint whose policy names no scheme.
            _ = services.AddAuthorizationBuilder()
                .SetFallbackPolicy(SessionPolicy())
                .AddPolicy(TellmaPolicies.Web, SessionPolicy())
                .AddPolicy(TellmaPolicies.Api, BearerPolicy(TellmaClaimTypes.ApiScope, AudienceResource.PublicOrigin))
                .AddPolicy(TellmaPolicies.Mcp, BearerPolicy(TellmaClaimTypes.ApiScope, AudienceResource.TenantMcp))
                .AddPolicy(TellmaPolicies.ControlPlane, BearerPolicy(TellmaClaimTypes.ControlPlaneScope, AudienceResource.PublicOrigin));
            _ = services.AddSingleton<IAuthorizationHandler, BearerRequirementHandler>();
        }

        /// <summary>The session policy: an authenticated session cookie.</summary>
        /// <returns>The policy.</returns>
        private static AuthorizationPolicy SessionPolicy()
        {
            return new AuthorizationPolicyBuilder(TellmaAuthentication.SessionScheme)
                .RequireAuthenticatedUser()
                .Build();
        }

        /// <summary>A bearer policy: an authenticated bearer token with a scope, for a resource.</summary>
        /// <param name="scope">The scope required.</param>
        /// <param name="audience">The resource the audience must name.</param>
        /// <returns>The policy.</returns>
        private static AuthorizationPolicy BearerPolicy(string scope, AudienceResource audience)
        {
            return new AuthorizationPolicyBuilder(TellmaAuthentication.BearerScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(scope), new AudienceRequirement(audience))
                .Build();
        }

        /// <summary>
        ///     Configures the session cookie: its name and attributes, the catalog session store and the
        ///     session events, whose answers never redirect.
        /// </summary>
        /// <param name="options">The cookie options.</param>
        /// <param name="session">The session options.</param>
        /// <param name="store">The catalog session store.</param>
        /// <param name="cookies">Whether the host's cookies are restricted to HTTPS.</param>
        private static void ConfigureSession(
            CookieAuthenticationOptions options, IOptions<TellmaSessionOptions> session, ICatalogSessionStore store, SessionCookieSecurity cookies)
        {
            // --- The cookie: host-only, HTTPS-only, script-proof, sent on top-level navigations ---
            // A browser refuses a __Host- cookie without Secure, which plain-HTTP Development cannot set.
            options.Cookie.Name = cookies.Secure ? TellmaAuthentication.SessionCookieName : TellmaAuthentication.DevelopmentSessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookies.Secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = session.Value.IdleLifetime;
            options.SlidingExpiration = true;

            // --- The ticket lives in the catalog; the cookie carries its key ---
            options.SessionStore = store;
            options.EventsType = typeof(TellmaSessionEvents);
        }

        /// <summary>Configures the sign-in scheme: the authorization code flow with PKCE and pushed requests, against the identity server.</summary>
        /// <param name="options">The OpenID Connect options.</param>
        /// <param name="slug">The distribution's slug, the client's id.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="identity">The identity options.</param>
        /// <param name="environment">The host environment.</param>
        /// <param name="secrets">The clients' secrets.</param>
        /// <param name="cookies">Whether the host's cookies are restricted to HTTPS.</param>
        private static void ConfigureSignIn(
            OpenIdConnectOptions options,
            string slug,
            IConfiguration configuration,
            IdentityHostOptions identity,
            IHostEnvironment environment,
            IdentityClientSecrets secrets,
            SessionCookieSecurity cookies)
        {
            // --- The authority and the client; options validation reports what is missing ---
            string? origin = configuration[HostConfigurationKeys.PublicOrigin];
            if (!string.IsNullOrWhiteSpace(origin) && (identity.Mode == IdentityMode.InProc || !string.IsNullOrWhiteSpace(identity.Authority)))
            {
                options.Authority = identity.ResolveAuthority(origin);
            }

            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.ClientId = slug;
            options.ClientSecret = secrets.ClientSecret;
            options.SignInScheme = TellmaAuthentication.SessionScheme;
            options.SignOutScheme = TellmaAuthentication.SessionScheme;

            // --- The flow: the code, with PKCE, pushed to the authority, which fails closed without it ---
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Require;
            options.Scope.Clear();
            foreach (string scope in SignInScopes)
            {
                options.Scope.Add(scope);
            }

            // --- The tokens and claims: kept as issued, the principal trimmed by the events ---
            options.SaveTokens = true;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.ClaimActions.Clear();
            options.TokenValidationParameters.NameClaimType = TellmaClaimTypes.Subject;
            options.DisableTelemetry = true;

            // --- The callbacks; the front-channel sign-out path stays unused ---
            options.CallbackPath = "/signin-oidc";
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            options.SignedOutRedirectUri = SignedOutPath;
            options.RemoteSignOutPath = PathString.Empty;

            // --- The flow's own cookies ride the top-level redirect back, like the session cookie ---
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = cookies.Secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.NonceCookie.SecurePolicy = cookies.Secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

            options.EventsType = typeof(BffOidcEvents);
        }

        /// <summary>Configures the bearer scheme against the identity server's keys and access-token rules.</summary>
        /// <param name="options">The bearer options.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="identity">The identity options.</param>
        /// <param name="environment">The host environment.</param>
        private static void ConfigureBearer(
            JwtBearerOptions options, IConfiguration configuration, IOptions<IdentityHostOptions> identity, IHostEnvironment environment)
        {
            // The authority is the issuer the session's sign-in uses; options validation reports a
            // missing one, so the scheme is left without it rather than failing here.
            string? origin = configuration[HostConfigurationKeys.PublicOrigin];
            IdentityHostOptions settings = identity.Value;
            if (!string.IsNullOrWhiteSpace(origin) && (settings.Mode == IdentityMode.InProc || !string.IsNullOrWhiteSpace(settings.Authority)))
            {
                options.Authority = settings.ResolveAuthority(origin);
            }

            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.MapInboundClaims = false;
            options.SaveToken = false;

            // --- Access tokens: typed, issuer-checked, short-lived; the audience is the policy's ---
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.ValidateAudience = false;
            options.TokenValidationParameters.ValidTypes = ["at+jwt"];
            options.TokenValidationParameters.NameClaimType = TellmaClaimTypes.Subject;
            options.TokenValidationParameters.ClockSkew = ClockSkew;
        }
    }
}
