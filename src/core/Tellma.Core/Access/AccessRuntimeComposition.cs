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
using Tellma.Core.Provisioning;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     Registers what permission evaluation needs beyond <see cref="CoreFeature" />'s contribution:
    ///     the access options, the permission classifier, the set builder and its instruments; the
    ///     user connection: its caches, the scoped connector and runner, and the connect initializer;
    ///     the scoped evaluator, its witness, the holders resolver, the administrator directory and
    ///     the security tables' guards; and the first administrator's bootstrap and the permission
    ///     drift scan.
    /// </summary>
    internal static class AccessRuntimeComposition
    {
        /// <summary>Registers the options, the builder, the instruments, the user connection and the evaluation.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition, whose user and role leaves close the holders resolver.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
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
            services.TryAddScoped<RecomposeLog>();
            services.TryAddScoped<IGuardedBatchRunner, GuardedBatchRunner>();

            // --- The connect initializer at 100, and the connected profile the negotiation at 200 reads (§7.8) ---
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRequestContextInitializer, ConnectInitializer>());
            services.TryAddScoped<IUserLocalePreferences, ConnectedUserPreferences>();

            // --- The evaluator and its witness (§5.1, §4.6), under the bar a sensitive securable asks (§4.3);
            // a web host replaces the bar with its configured session bar ---
            services.TryAddSingleton(StepUpBar.Default);
            services.TryAddScoped<AccessWitness>();
            services.TryAddScoped<AccessEvaluator>();
            services.TryAddScoped<IAccessEvaluator>(static provider => provider.GetRequiredService<AccessEvaluator>());

            // --- "Who holds this", closed over the distribution's user and role leaves, and the administrators (§10) ---
            services.TryAddSingleton<HoldersComposer>();
            services.TryAddScoped(
                typeof(IAccessHoldersResolver),
                typeof(AccessHoldersResolver<,>).MakeGenericType(record.LeafOf(typeof(User)), record.LeafOf(typeof(Role))));
            services.TryAddScoped<IAdministratorDirectory, AdministratorDirectory>();

            // --- The security tables' guards: the lock and the three invariants (§8.3) ---
            services.TryAddScoped<IAccessGuards, AccessGuards>();

            // --- The first administrator's bootstrap, closed over the user leaf, and the held development subjects (§11) ---
            services.TryAddScoped<DevelopmentSubjects>();
            services.TryAddScoped<IDevelopmentSubjects>(static provider => provider.GetRequiredService<DevelopmentSubjects>());
            services.TryAddScoped(typeof(ITenantBootstrapper), typeof(TenantBootstrapper<>).MakeGenericType(record.LeafOf(typeof(User))));

            // --- The drift scan, and the migrator's report of it after migrate (§9) ---
            services.TryAddScoped<PermissionDriftScanner>();
            services.TryAddScoped<IPermissionDriftScanner>(static provider => provider.GetRequiredService<PermissionDriftScanner>());
            services.TryAddScoped<ITenantDriftReport>(static provider => provider.GetRequiredService<PermissionDriftScanner>());
        }
    }
}
