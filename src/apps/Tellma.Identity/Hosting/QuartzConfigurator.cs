// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Tellma.Identity.Services.Invitations;
using Tellma.Identity.Services.Sessions;

namespace Tellma.Identity.Hosting
{
    /// <summary>
    ///     Registers the Quartz scheduler and the engine's recurring jobs: OpenIddict's
    ///     token/authorization prune, registered by OpenIddict itself, plus the session prune and
    ///     the invitation recovery sweep, registered here.
    /// </summary>
    internal static class QuartzConfigurator
    {
        /// <summary>How often the session sweep runs.</summary>
        private static readonly TimeSpan SessionPruneInterval = TimeSpan.FromHours(1);

        /// <summary>How long after startup the first session sweep runs.</summary>
        private static readonly TimeSpan SessionPruneStartDelay = TimeSpan.FromMinutes(5);

        /// <summary>How often the invitation recovery sweep runs.</summary>
        private static readonly TimeSpan InvitationDispatchInterval = TimeSpan.FromMinutes(2);

        /// <summary>
        ///     How long after startup the first invitation sweep runs. Short: the invitations this
        ///     recovers are exactly the ones a crash left behind, and the restart that follows is
        ///     the first chance to send them.
        /// </summary>
        private static readonly TimeSpan InvitationDispatchStartDelay = TimeSpan.FromSeconds(30);

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
                // The session sweep. Delayed first run so startup is not competing with a table
                // scan, and hourly after that: the windows this job enforces are measured in days,
                // so a sweep any more often would only cost reads.
                JobKey prune = new(SessionPruneJob.Name);
                quartz.AddJob<SessionPruneJob>(job => job.WithIdentity(prune));
                quartz.AddTrigger(trigger => trigger
                    .ForJob(prune)
                    .WithIdentity(SessionPruneJob.Name + ".Trigger")
                    .StartAt(DateBuilder.FutureDate((int)SessionPruneStartDelay.TotalMinutes, IntervalUnit.Minute))
                    .WithSimpleSchedule(schedule => schedule
                        .WithInterval(SessionPruneInterval)
                        .RepeatForever()));

                // The invitation recovery sweep. Unlike the prune, this one has a deadline that
                // matters to a person: an invitation whose mail was lost is only found here, and
                // its recipient is not waiting for it and cannot ask again. So it starts soon
                // after boot — the moment a crash's leftovers can be picked up — and runs often.
                JobKey dispatch = new(InvitationDispatchJob.Name);
                quartz.AddJob<InvitationDispatchJob>(job => job.WithIdentity(dispatch));
                quartz.AddTrigger(trigger => trigger
                    .ForJob(dispatch)
                    .WithIdentity(InvitationDispatchJob.Name + ".Trigger")
                    .StartAt(DateBuilder.FutureDate(
                        (int)InvitationDispatchStartDelay.TotalSeconds, IntervalUnit.Second))
                    .WithSimpleSchedule(schedule => schedule
                        .WithInterval(InvitationDispatchInterval)
                        .RepeatForever()));
            });

            if (!alreadyScheduled)
            {
                services.AddQuartzHostedService(static quartz => quartz.WaitForJobsToComplete = true);
            }
        }
    }
}
