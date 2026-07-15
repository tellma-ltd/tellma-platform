// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Hosting
{
    /// <summary>
    ///     The single registration path for the identity engine. Both hosting shapes — the
    ///     standalone <c>Tellma.Identity.Web</c> host and a distribution host running the engine
    ///     in-proc — call the same <c>AddTellmaIdentity</c>, so behavior cannot drift between
    ///     them.
    /// </summary>
    public static class TellmaIdentityServiceCollectionExtensions
    {
        /// <summary>Registers the identity engine from a configuration section.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">
        ///     The engine's configuration section (conventionally <c>TellmaIdentity</c>).
        /// </param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddTellmaIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            return AddTellmaIdentity(services, configuration.Bind);
        }

        /// <summary>Registers the identity engine with code-based configuration.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configures the engine options.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddTellmaIdentity(this IServiceCollection services, Action<TellmaIdentityOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            // Registration-time decisions (endpoint URIs, cookie paths, route prefixes, key
            // loading) need the option values now, before the host is built. The same delegate is
            // registered for runtime resolution, and the snapshot is validated immediately so a
            // misconfigured host fails at startup with the full failure list.
            TellmaIdentityOptions snapshot = new();
            configure(snapshot);

            ValidateOptionsResult validation = new TellmaIdentityOptionsValidator().Validate(Microsoft.Extensions.Options.Options.DefaultName, snapshot);
            if (validation.Failed)
            {
                throw new InvalidOperationException(
                    "Tellma Identity configuration is invalid: " + string.Join(" ", validation.Failures ?? []));
            }

            services.AddOptions<TellmaIdentityOptions>().Configure(configure).ValidateOnStart();
            services.AddSingleton<IValidateOptions<TellmaIdentityOptions>>(
                static provider => new TellmaIdentityOptionsValidator(
                    provider.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>()));

            services.AddHttpContextAccessor();
            services.TryAddSingleton(TimeProvider.System);

            // Startup seeding runs before Quartz (hosted services start in registration order),
            // so pruning never races a not-yet-migrated store.
            services.AddScoped<Services.Seeding.IdentitySeeder>();
            services.AddHostedService<Services.Seeding.IdentitySeedHostedService>();

            // Engine services.
            services.AddScoped<Services.Audit.IAuditLogger, Services.Audit.SqlAuditLogger>();
            services.AddScoped<Services.Provisioning.IClientProvisioningService, Services.Provisioning.ClientProvisioningService>();
            services.AddSingleton<Services.AuthenticationPolicy.IAuthenticationPolicyService, Services.AuthenticationPolicy.AuthenticationPolicyService>();
            services.AddScoped<Services.AuthenticationPolicy.TellmaSignInService>();
            services.AddScoped<Services.AuthenticationPolicy.TellmaPrincipalFactory>();
            services.AddScoped<Services.Sessions.ISessionRegistry, Services.Sessions.SqlSessionRegistry>();
            services.AddScoped<Services.EmailCodes.IEmailCodeService, Services.EmailCodes.EmailCodeService>();
            services.AddScoped<Services.RateLimiting.IRateLimitCounterStore, Services.RateLimiting.SqlRateLimitCounterStore>();
            services.AddScoped<Services.Email.EmailTemplateService>();

            // Outbound mail leaves the request path via a background worker so enumeration-safe
            // endpoints return in constant time.
            services.AddSingleton<Services.Email.EmailDispatcher>();
            services.AddSingleton<Services.Email.IEmailDispatcher>(
                static provider => provider.GetRequiredService<Services.Email.EmailDispatcher>());
            services.AddHostedService<Services.Email.EmailDispatchHostedService>();
            services.AddSingleton<IBrandingResolver, DefaultBrandingResolver>();
            services.AddSingleton<Services.BackchannelLogout.LogoutTokenFactory>();
            services.AddScoped<Services.BackchannelLogout.IBackchannelLogoutService, Services.BackchannelLogout.BackchannelLogoutService>();
            services.AddScoped<Services.Tokens.IOneTimeTokenService, Services.Tokens.OneTimeTokenService>();
            services.AddScoped<Services.Invitations.InvitationService>();
            services.AddScoped<Services.Tap.ITemporaryAccessPassService, Services.Tap.TemporaryAccessPassService>();
            services.AddSingleton<IdentityMetrics>();

            // Management-API authorization policies (tellma_identity, tellma_control_plane).
            services.AddAuthorization();
            services.Configure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(ApiPolicies.Configure);

            // Best-effort logout-token delivery: bounded timeout plus the standard retry pipeline.
            services.AddHttpClient(
                    Services.BackchannelLogout.BackchannelLogoutService.HttpClientName,
                    static client => client.Timeout = TimeSpan.FromSeconds(15))
                .AddStandardResilienceHandler();

            // The Development sink is the only email difference from a deployed instance.
            if (snapshot.Development.UseEmailSink)
            {
                services.AddSingleton<Services.Email.IEmailSender, Services.Email.LogSinkEmailSender>();
            }
            else
            {
                services.AddScoped<Services.Email.IEmailSender, Services.Email.SmtpEmailSender>();
            }

            services.AddLocalization();

            // The identity store. An in-proc host points the store at its own database through
            // ConfigureDbContext; the engine's tables live in the dedicated schema either way.
            services.AddDbContext<TellmaIdentityDbContext>((provider, builder) =>
            {
                TellmaIdentityOptions options = provider.GetRequiredService<IOptions<TellmaIdentityOptions>>().Value;
                if (options.ConfigureDbContext is { } configureDbContext)
                {
                    configureDbContext(provider, builder);
                }
                else
                {
                    builder.UseSqlServer(
                        options.ConnectionString,
                        sql => sql.MigrationsAssembly(TellmaIdentityConstants.MigrationsAssemblyName));
                }
            });

            IdentityConfigurator.Configure(services, snapshot);
            OpenIddictConfigurator.Configure(services, snapshot);
            DataProtectionConfigurator.Configure(services, snapshot);
            QuartzConfigurator.Configure(services);

            // MVC surfaces: protocol controllers (with views for consent/verification/logout
            // interactions) and the account UI Razor Pages. The engine assembly is added as an
            // application part explicitly — automatic discovery of RCL controllers depends on
            // build-time attribute injection that must not be load-bearing. In in-proc mode every
            // engine route is prefixed with the reserved path base; host routes are untouched.
            services.AddControllersWithViews()
                .AddApplicationPart(typeof(TellmaIdentityServiceCollectionExtensions).Assembly);
            services.AddRazorPages();

            // The operator (control-plane) surface exists only in standalone mode; remove it from
            // the application model in-proc so its routes do not exist there at all.
            services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(mvc =>
                mvc.Conventions.Add(new RemoveControlPlaneControllersConvention(snapshot.Mode)));

            string prefix = snapshot.RoutePrefix;
            if (prefix.Length > 0)
            {
                services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(mvc =>
                    mvc.Conventions.Add(new TellmaIdentityControllerRouteConvention(prefix)));
                services.Configure<Microsoft.AspNetCore.Mvc.RazorPages.RazorPagesOptions>(pages =>
                    pages.Conventions.Add(new TellmaIdentityPageRouteConvention(prefix)));
            }

            return services;
        }
    }
}
