// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Problems;

namespace Tellma.Core.AspNetCore.Authentication
{
    /// <summary>
    ///     Registers the host's authentication schemes and authorization policies. No scheme is the
    ///     default: every policy names its scheme, so a cookie never authenticates a bearer surface and
    ///     a bearer never authenticates the web surface, and an endpoint with no policy of its own falls
    ///     to the session-cookie fallback.
    /// </summary>
    internal static class TellmaAuthenticationComposition
    {
        /// <summary>The bearer handler's tolerance for clock drift, against ten-minute access tokens.</summary>
        internal static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);

        /// <summary>Registers the schemes, the policies and the bearer requirements' handler.</summary>
        /// <param name="services">The services.</param>
        public static void Register(IServiceCollection services)
        {
            // --- The schemes ---
            _ = services
                .AddAuthentication()
                .AddCookie(TellmaAuthentication.SessionScheme)
                .AddJwtBearer(TellmaAuthentication.BearerScheme);

            _ = services.AddOptions<CookieAuthenticationOptions>(TellmaAuthentication.SessionScheme)
                .Configure<IConfiguration, IOptions<TellmaSessionOptions>>(ConfigureSession);
            _ = services.AddOptions<JwtBearerOptions>(TellmaAuthentication.BearerScheme)
                .Configure<IConfiguration, IOptions<IdentityHostOptions>, IHostEnvironment>(ConfigureBearer);

            // --- The policies: each names its scheme ---
            _ = services.AddAuthorizationBuilder()
                .SetFallbackPolicy(SessionPolicy())
                .SetDefaultPolicy(SessionPolicy())
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

        /// <summary>Configures the session cookie: its name and attributes, and answers that never redirect.</summary>
        /// <param name="options">The cookie options.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="session">The session options.</param>
        private static void ConfigureSession(CookieAuthenticationOptions options, IConfiguration configuration, IOptions<TellmaSessionOptions> session)
        {
            // --- The cookie: host-only, HTTPS-only, script-proof, sent on top-level navigations ---
            // A browser refuses a __Host- cookie without Secure, which plain-HTTP Development cannot set.
            bool plainHttp = Uri.TryCreate(configuration[HostConfigurationKeys.PublicOrigin], UriKind.Absolute, out Uri? origin)
                && origin.Scheme == Uri.UriSchemeHttp;
            options.Cookie.Name = plainHttp ? TellmaAuthentication.DevelopmentSessionCookieName : TellmaAuthentication.SessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = plainHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.Cookie.IsEssential = true;
            options.ExpireTimeSpan = session.Value.IdleLifetime;
            options.SlidingExpiration = true;

            // --- An API answers 401 and 403; it never redirects to a sign-in page ---
            options.Events.OnRedirectToLogin = static context => ProblemWriter.WriteAsync(
                context.HttpContext, StatusCodes.Status401Unauthorized, "unauthenticated");
            options.Events.OnRedirectToAccessDenied = static context => ProblemWriter.WriteAsync(
                context.HttpContext, StatusCodes.Status403Forbidden, "forbidden");
            options.Events.OnRedirectToLogout = static context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToReturnUrl = static context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            };
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
