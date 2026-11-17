// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Localization;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     Registers what permission evaluation needs beyond <see cref="CoreFeature" />'s contribution:
    ///     the access options, the permission classifier, the set builder and its instruments, and the
    ///     user connection: its caches, the scoped connector and runner, and the connect initializer.
    /// </summary>
    internal static class AccessRuntimeComposition
    {
        /// <summary>Registers the options, the builder, the instruments and the user connection.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation ---
            options.Bind<AccessOptions>(AccessOptions.SectionPath, CoreFeature.FeatureName);

            // --- The instruments and the logger; AddMetrics and AddLogging are idempotent ---
            _ = services.AddMetrics();
            _ = services.AddLogging();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<AccessMetrics>();

            // --- The classifier and the set builder, singletons over the frozen registry ---
            services.TryAddSingleton<PermissionClassifier>();
            services.TryAddSingleton<UserAccessBuilder>();

            // --- The user connection's process-wide caches (spec 0014 §6.2, §6.3) ---
            services.TryAddSingleton<ConnectCache>();
            services.TryAddSingleton<PermissionsCache>();
            services.TryAddSingleton<ProfileCache>();
            services.TryAddSingleton<ConnectorServices>();

            // --- The scope's connector, under its contract and the runner's seam, and the runner (§7.1) ---
            services.TryAddScoped<UserConnector>();
            services.TryAddScoped<IUserConnector>(static provider => provider.GetRequiredService<UserConnector>());
            services.TryAddScoped<IScopeConnection>(static provider => provider.GetRequiredService<UserConnector>());
            services.TryAddScoped<IGuardedBatchRunner, GuardedBatchRunner>();

            // --- The connect initializer at 100, and the connected profile the negotiation at 200 reads (§7.8) ---
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRequestContextInitializer, ConnectInitializer>());
            services.TryAddScoped<IUserLocalePreferences, ConnectedUserPreferences>();
        }
    }
}
