// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;

namespace Tellma.Core.Data.Batches
{
    /// <summary>Registers the data layer's runtime: the options, the executor, the tenant database and its factory.</summary>
    internal static class DataComposition
    {
        /// <summary>Registers the data layer.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation ---
            options.Bind<DataOptions>(DataOptions.SectionPath, CoreFeature.FeatureName);

            // --- The executor, and the seams later features replace ---
            // TryAdd for the owners: the stack registry's composition maps tables to their stacks.
            services.TryAddSingleton<IStackTableOwners, NoStackTableOwners>();
            _ = services.AddSingleton<DataExecutor>();

            // --- Per unit of work: the counters, the cancellation token and the tenant database ---
            // TryAdd for the scope values: a host that opens scopes of its own may register them first.
            services.TryAddScoped<DataAccessScope>();
            services.TryAddScoped<DataCancellation>();
            _ = services.AddScoped<ITenantDatabase, TenantDatabase>();

            // --- Outside any tenant scope: the factory of maintenance handles ---
            _ = services.AddSingleton<ITenantDatabaseFactory, TenantDatabaseFactory>();
        }
    }
}
