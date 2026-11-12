// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Registers the tenancy runtime the composition root installs in every host: the catalog
    ///     model, the options of <c>Tellma:Catalog</c> and <c>Tellma:Sql</c>, the registry and its
    ///     refresh service, the connection factory, the catalog's write side, the membership
    ///     directory, and the request context with its two writers and the services that read it.
    /// </summary>
    internal static class TenancyComposition
    {
        /// <summary>Registers the tenancy runtime, after every feature's contribution.</summary>
        /// <param name="services">The host's services.</param>
        /// <param name="record">The realised composition: its slug and environment.</param>
        /// <param name="options">Binds the platform's options, validated by the startup gate.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation ---
            options.Bind<TellmaCatalogOptions>(TellmaCatalogOptions.SectionPath, CoreFeature.FeatureName);
            options.Bind<TellmaSqlOptions>(TellmaSqlOptions.SectionPath, CoreFeature.FeatureName);
            _ = services.AddOptions<TenantRegistryStartupOptions>();

            // --- What the runtime needs of any host: a clock, logging and metrics ---
            services.TryAddSingleton(TimeProvider.System);
            _ = services.AddLogging();
            _ = services.AddMetrics();

            // --- The composition the runtime serves ---
            _ = services.AddSingleton(new TenancyEnvironment(record.Slug, record.Environment.IsDevelopment()));
            _ = services.AddSingleton<TenancyMetrics>();

            // --- The catalog: its model and connection, the registry over it and the refresh loop ---
            _ = services.AddSingleton<CatalogModel>();
            _ = services.AddSingleton<CatalogConnection>();
            services.TryAddSingleton<ICatalogReader, SqlCatalogReader>();
            _ = services.AddSingleton<TenantRegistry>();
            _ = services.AddSingleton<ITenantRegistry>(static provider => provider.GetRequiredService<TenantRegistry>());
            _ = services.AddHostedService<TenantRegistryRefreshService>();

            // --- Connections, the write side and the membership hints ---
            _ = services.AddSingleton<ITenantConnectionFactory, TenantConnectionFactory>();
            _ = services.AddSingleton<ITenantCatalog, TenantCatalog>();
            _ = services.AddSingleton<TenantMembershipDirectory>();
            _ = services.AddSingleton<ITenantMembershipDirectory>(static provider => provider.GetRequiredService<TenantMembershipDirectory>());

            // --- Provisioning of tenants registered at run time: none until a deployment registers one ---
            services.TryAddSingleton<ITenantProvisioningTrigger, NotConfiguredProvisioningTrigger>();

            // --- The request context: one holder per scope, read through the accessor (spec 0011 §4.2) ---
            _ = services.AddSingleton<RequestContextDefaults>();
            _ = services.AddScoped<RequestContextHolder>();
            _ = services.AddScoped<IRequestContextHolder>(static provider => provider.GetRequiredService<RequestContextHolder>());
            _ = services.AddScoped<IRequestContextAccessor>(static provider => provider.GetRequiredService<RequestContextHolder>());

            // --- Its two writers: the access guard of a request and the scope factory of background work ---
            _ = services.AddScoped<ITenantAccessGuard, TenantAccessGuard>();
            _ = services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();

            // --- What reads the bound tenant: the scope's connections and the sandbox flag ---
            // Added, not TryAdded: in a tenant host the sandbox flag is the bound tenant's category, so
            // a never-sandboxed context registered before the composition must not survive it.
            _ = services.AddScoped<ITenantConnectionProvider, TenantConnectionProvider>();
            _ = services.AddScoped<ISandboxContext, TenantSandboxContext>();
        }
    }
}
