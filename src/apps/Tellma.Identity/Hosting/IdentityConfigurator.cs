// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;
using Tellma.Identity.Options;

namespace Tellma.Identity.Hosting
{
    /// <summary>
    ///     Registers ASP.NET Core Identity: the user store, credential handling, passkeys, the
    ///     SSO session cookie, and the config-gated external login providers.
    /// </summary>
    internal static class IdentityConfigurator
    {
        /// <summary>Registers Identity and its cookie/passkey configuration.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options">The registration-time options snapshot.</param>
        public static void Configure(IServiceCollection services, TellmaIdentityOptions options)
        {
            services
                .AddIdentity<TellmaIdentityUser, IdentityRole>(identity =>
                {
                    TellmaIdentityModelDefaults.ConfigureStoreOptions(identity);

                    identity.User.RequireUniqueEmail = true;

                    // Length over composition, per NIST guidance; passwords are off by default
                    // anyway and only ever an opt-in method.
                    identity.Password.RequiredLength = 12;
                    identity.Password.RequireDigit = false;
                    identity.Password.RequireLowercase = false;
                    identity.Password.RequireUppercase = false;
                    identity.Password.RequireNonAlphanumeric = false;

                    identity.Lockout.AllowedForNewUsers = true;
                    identity.Lockout.MaxFailedAccessAttempts = 5;
                    identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                })
                .AddEntityFrameworkStores<TellmaIdentityDbContext>()
                // Deliberately not AddDefaultTokenProviders(): the built-in email/phone providers
                // are TOTP-based and replayable within their window; the engine's single-use codes
                // replace them. Only the Data Protection provider (security-sensitive operations)
                // and the authenticator (TOTP second factor) are registered.
                .AddTokenProvider<DataProtectorTokenProvider<TellmaIdentityUser>>(TokenOptions.DefaultProvider)
                .AddTokenProvider<AuthenticatorTokenProvider<TellmaIdentityUser>>(TokenOptions.DefaultAuthenticatorProvider);

            // The SSO session cookie: "remember me" persists it (sliding); otherwise it is a
            // browser-session cookie. Short stamp revalidation gives sign-out-everywhere and
            // policy changes a tight upper bound.
            string prefix = options.PathBase;
            services.ConfigureApplicationCookie(cookie =>
            {
                cookie.Cookie.Name = TellmaIdentityConstants.SsoCookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = options.Development.AllowInsecureHttp
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                cookie.LoginPath = prefix + "/Identity/Account/Login";
                cookie.LogoutPath = prefix + "/Identity/Account/Logout";
                cookie.AccessDeniedPath = prefix + "/Identity/Account/AccessDenied";
                cookie.ExpireTimeSpan = TimeSpan.FromDays(14);
                cookie.SlidingExpiration = true;
            });

            services.Configure<SecurityStampValidatorOptions>(static validator =>
                validator.ValidationInterval = TimeSpan.FromMinutes(5));

            // Passkeys: discoverable resident keys with user verification, scoped to the issuer
            // host (the authority origin in standalone mode, so one passkey works across every
            // distribution).
            services.Configure<IdentityPasskeyOptions>(passkey =>
            {
                passkey.ServerDomain = !string.IsNullOrWhiteSpace(options.PasskeyServerDomain)
                    ? options.PasskeyServerDomain
                    : options.Issuer!.Host;
                passkey.ResidentKeyRequirement = "required";
                passkey.UserVerificationRequirement = "required";
            });

            // External providers register only when configured; the sign-in UI additionally
            // filters them by the tenant's allowed-methods list.
            AuthenticationBuilder authentication = services.AddAuthentication();
            if (options.ExternalProviders.Google.IsConfigured)
            {
                authentication.AddGoogle(google =>
                {
                    google.ClientId = options.ExternalProviders.Google.ClientId!;
                    google.ClientSecret = options.ExternalProviders.Google.ClientSecret ?? string.Empty;
                });
            }

            if (options.ExternalProviders.Microsoft.IsConfigured)
            {
                authentication.AddMicrosoftAccount(microsoft =>
                {
                    microsoft.ClientId = options.ExternalProviders.Microsoft.ClientId!;
                    microsoft.ClientSecret = options.ExternalProviders.Microsoft.ClientSecret ?? string.Empty;
                });
            }
        }
    }
}
