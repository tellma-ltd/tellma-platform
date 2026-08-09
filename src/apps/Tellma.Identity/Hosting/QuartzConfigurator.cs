// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Tellma.Identity.Services.Sessions;

namespace Tellma.Identity.Hosting
{
    /// <summary>
    ///     Registers the Quartz scheduler and the recurring jobs that keep the store from growing
    ///     without bound: OpenIddict's token/authorization prune, registered by OpenIddict itself,
    ///     and the engine's own session prune, registered here.
    /// </summary>
    internal static class QuartzConfigurator
    {
        /// <summary>How often the session sweep runs.</summary>
        private static readonly TimeSpan SessionPruneInterval = TimeSpan.FromHours(1);

        /// <summary>How long after startup the first session sweep runs.</summary>
        private static readonly TimeSpan SessionPruneStartDelay = TimeSpan.FromMinutes(5);

        /// <summary>Registers the engine's jobs, and Quartz's hosted service once.</summary>
        /// <param name="services">The service collection.</param>
        public static void Configure(IServiceCollection services)
        {
            // Job registration is additive and safe to repeat; only the hosted service that
            // actually starts a scheduler must be singular, so an in-proc host already running
            // Quartz keeps its own and still gets our jobs.
            bool alreadyScheduled = services.Any(static descriptor => descriptor.ServiceType == typeof(ISchedulerFactory));

            services.AddQuartz(static quartz =>
            {
                JobKey key = new(SessionPruneJob.Name);
                quartz.AddJob<SessionPruneJob>(job => job.WithIdentity(key));

                // Delayed first run so startup is not competing with a table scan, and hourly
                // after that: the windows this job enforces are measured in days, so a sweep any
                // more often would only cost reads.
                quartz.AddTrigger(trigger => trigger
                    .ForJob(key)
                    .WithIdentity(SessionPruneJob.Name + ".Trigger")
                    .StartAt(DateBuilder.FutureDate((int)SessionPruneStartDelay.TotalMinutes, IntervalUnit.Minute))
                    .WithSimpleSchedule(schedule => schedule
                        .WithInterval(SessionPruneInterval)
                        .RepeatForever()));
            });

            if (!alreadyScheduled)
            {
                services.AddQuartzHostedService(static quartz => quartz.WaitForJobsToComplete = true);
            }
        }
    }
}
