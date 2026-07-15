// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Tellma.Identity.Hosting
{
    /// <summary>
    ///     Registers the Quartz scheduler and hosted service that run OpenIddict's
    ///     token/authorization pruning job. Guarded so an in-proc host that already runs Quartz
    ///     is not double-registered.
    /// </summary>
    internal static class QuartzConfigurator
    {
        /// <summary>Registers Quartz and its hosted service once.</summary>
        /// <param name="services">The service collection.</param>
        public static void Configure(IServiceCollection services)
        {
            if (services.Any(static descriptor => descriptor.ServiceType == typeof(ISchedulerFactory)))
            {
                return;
            }

            services.AddQuartz();
            services.AddQuartzHostedService(static quartz => quartz.WaitForJobsToComplete = true);
        }
    }
}
