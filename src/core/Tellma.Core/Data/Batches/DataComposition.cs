// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    /// <summary>Registers the data layer's runtime: the options, the executor and its instruments, the query services, the tenant database and its factory.</summary>
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

            // --- The query statements: one engine, whose caches every compile shares, and the schemas ---
            // TryAdd for the engine: a host that tunes its options registers it first.
            services.TryAddSingleton(static _ => new QueryexEngine());
            _ = services.AddSingleton<QueryexSchemaProvider>();
            _ = services.AddSingleton<IQueryexSchemaProvider>(static services => services.GetRequiredService<QueryexSchemaProvider>());
            _ = services.AddSingleton<EntityMaterializer>();
            _ = services.AddSingleton<QueryServices>();
            _ = services.AddSingleton<IDisplayNames, DisplayNames>();

            // AddMetrics is idempotent and supplies the IMeterFactory the executor's instruments are created from.
            _ = services.AddMetrics();
            _ = services.AddSingleton<DataMetrics>();

            // --- Per unit of work: the counters, the cancellation token and the tenant database ---
            // The recorder owns the counters, so whichever resolves them first (the opener naming the
            // operation, or a batch) creates it, and disposing the scope records the round trips, zero
            // included. TryAdd for the cancellation token: a host that opens scopes of its own may
            // register it first.
            services.TryAddScoped<DataAccessRecorder>();
            services.TryAddScoped(static services => services.GetRequiredService<DataAccessRecorder>().Scope);
            services.TryAddScoped<DataCancellation>();
            _ = services.AddScoped<ITenantDatabase, TenantDatabase>();

            // --- Outside any tenant scope: the factory of maintenance handles ---
            _ = services.AddSingleton<ITenantDatabaseFactory, TenantDatabaseFactory>();
        }
    }
}
