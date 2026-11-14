// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Composition;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     Registers what background jobs need beyond <see cref="CoreFeature" />'s contribution: the
    ///     options, validated by the startup gate; the handler registry built from what the realizers
    ///     recorded; the seam through which an enqueue wakes this instance's poller; and the scoped
    ///     job queue.
    /// </summary>
    internal static class JobsComposition
    {
        /// <summary>Registers the options, the registry, the wake seam and the queue.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition, whose job contributions the registry reads.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation ---
            options.Bind<JobsOptions>(JobsOptions.SectionPath, CoreFeature.FeatureName);

            // --- The registry, built once the composition is realised; the wake seam, one per process ---
            _ = services.AddSingleton(_ => new JobHandlerRegistry(record));
            _ = services.AddSingleton<TenantPollerWake>();

            // --- The queue, per scope: it reads the scope's user and confirmations ---
            _ = services.AddScoped<IJobQueue, JobQueue>();
        }
    }
}
