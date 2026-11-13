// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Cronos;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     <c>jobs.schedules</c>: every built-in schedule's cron expression parses as five-field Unix
    ///     cron and has a next occurrence, in its own zone or, for a schedule that follows the tenant's
    ///     zone, in UTC.
    /// </summary>
    /// <remarks>
    ///     A zone this host's time-zone data cannot resolve is judged in UTC: whether the host knows
    ///     the zone is the time-zone check's to report, and an expression that never occurs in UTC
    ///     never occurs in any zone.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class JobSchedulesCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.JobSchedules;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            DateTimeOffset now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();

            foreach (ContributedBuiltInSchedule schedule in record.BuiltInSchedules)
            {
                BuiltInScheduleSeed seed = schedule.Seed;
                string described = string.Create(CultureInfo.InvariantCulture, $"The built-in schedule {seed.Id} of the feature '{schedule.Owner.Name}'");

                // --- The expression parses in the scheduler's dialect ---
                if (!CronExpression.TryParse(seed.Cron, CronFormat.Standard, out CronExpression? expression))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{described} has the cron expression '{seed.Cron}', which does not parse as five-field Unix cron.",
                        "Give minute, hour, day of month, month and day of week, such as '0 3 * * *' for three every morning, or a macro such as '@daily'."));
                    continue;
                }

                // --- It occurs again: an expression such as the 30th of February never fires ---
                TimeZoneInfo zone = seed.TimeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(seed.TimeZoneId, out TimeZoneInfo? found)
                    ? found
                    : TimeZoneInfo.Utc;
                if (expression.GetNextOccurrence(now, zone) is null)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{described} has the cron expression '{seed.Cron}', which has no next occurrence.",
                        "Give an expression that names a date that exists, such as '0 3 28 2 *' rather than '0 3 30 2 *'."));
                }
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
