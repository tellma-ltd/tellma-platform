// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     Registers what background jobs need beyond <see cref="CoreFeature" />'s contribution: the
    ///     options, validated by the startup gate; the handler registry built from what the realizers
    ///     recorded; the seam through which an enqueue wakes this instance's poller; the scoped job
    ///     queue; the machinery that runs what a claim leased, and the scheduler's tick; and, unless
    ///     <see cref="JobsOptions.Enabled" /> is configured off, the worker that polls the tenants.
    /// </summary>
    internal static class JobsComposition
    {
        /// <summary>Registers the options, the registry, the wake seam, the queue, the execution machinery, the tick and the worker.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="record">The realised composition, whose job contributions the registry reads.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation, the drain held below the host's shutdown timeout ---
            options.Bind<JobsOptions>(JobsOptions.SectionPath, CoreFeature.FeatureName);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JobsOptions>, JobsHostValidation>());

            // --- The registry, built once the composition is realised; the wake seam, one per process ---
            _ = services.AddSingleton(_ => new JobHandlerRegistry(record));
            _ = services.AddSingleton<TenantPollerWake>();

            // --- The queue, per scope: it reads the scope's user and confirmations ---
            _ = services.AddScoped<IJobQueue, JobQueue>();

            // --- A job's label in its reader's language, for the server's readers ---
            _ = services.AddSingleton<JobLabels>();

            // --- The execution machinery, one per process: the instruments, the leases this
            // instance holds, their renewal, the English rendering of keys for the log, the runner of
            // claims, the batch slots, the scheduler's tick, the poll round trip that carries it, the
            // renewal schedule and the dispatcher ---
            services.TryAddSingleton(TimeProvider.System);
            _ = services.AddSingleton<JobsMetrics>();
            _ = services.AddSingleton<JobLeases>();
            _ = services.AddSingleton<JobRenewal>();
            _ = services.AddSingleton<IJobRenewal>(static provider => provider.GetRequiredService<JobRenewal>());
            _ = services.AddSingleton<JobLogText>();
            _ = services.AddSingleton<JobRunner>();
            _ = services.AddSingleton<IJobClaimRunner>(static provider => provider.GetRequiredService<JobRunner>());
            _ = services.AddSingleton<JobConcurrency>();
            _ = services.AddSingleton<IScheduleTick, ScheduleTick>();
            _ = services.AddSingleton<JobPoll>();
            _ = services.AddSingleton<IJobPoll>(static provider => provider.GetRequiredService<JobPoll>());
            _ = services.AddSingleton<JobRenewalScheduler>();
            _ = services.AddSingleton<JobDispatcher>();

            // --- The worker, unless configured off: a hosted service, and a listener the registry
            // tells of every tenant state change; it reads the bound options again when it starts ---
            if (Enabled(record.Configuration))
            {
                _ = services.AddSingleton<JobWorker>();
                _ = services.AddSingleton<IHostedService>(static provider => provider.GetRequiredService<JobWorker>());
                _ = services.AddSingleton<ITenantStateListener>(static provider => provider.GetRequiredService<JobWorker>());
            }
        }

        /// <summary>Whether the configuration leaves the worker on: <see cref="JobsOptions.Enabled" /> is not configured false.</summary>
        /// <param name="configuration">The composition's configuration.</param>
        /// <returns>True unless the option reads false.</returns>
        internal static bool Enabled(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            string? value = configuration.GetSection(JobsOptions.SectionPath)[nameof(JobsOptions.Enabled)];
            return !bool.TryParse(value, out bool enabled) || enabled;
        }
    }
}
