// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Composition;
using Tellma.Core.Queryex;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     Realises a <see cref="BuiltInScheduleContributionItem" />: holds the id to the reserved band
    ///     and the platform's share of it, the key to the grammar and the zone to the platform's
    ///     time-zone set, serialises the arguments, and records the schedule for the migrations to seed.
    /// </summary>
    /// <remarks>
    ///     Ids and keys repeated across features, and keys a feature retired, are reported once every
    ///     item is realised, by <see cref="JobContributionChecks" />; an expression that does not parse
    ///     or never occurs is the <c>jobs.schedules</c> startup check's to report.
    /// </remarks>
    internal sealed class BuiltInScheduleContributionRealizer : IContributionRealizer<BuiltInScheduleContributionItem>
    {
        /// <summary>The highest id of the platform's own built-in schedules.</summary>
        public const int CoreBandEnd = 99;

        /// <summary>The time-zone ids a built-in may name, compared ordinally as the value set compares them.</summary>
        private static readonly HashSet<string> TimeZones = new(QueryexTimeZones.Ids, StringComparer.Ordinal);

        /// <inheritdoc />
        public void Realize(BuiltInScheduleContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(context);

            string described = string.Create(CultureInfo.InvariantCulture, $"The built-in schedule {item.Id} of the feature '{owner.Name}'");
            bool problems = false;

            // --- The id: in the reserved band, and in the platform's share only for the platform ---
            if (item.Id is < 1 or > WellKnownIds.ReservedIdBandEnd)
            {
                Report(context, $"{described} is outside the reserved band of 1 to {WellKnownIds.ReservedIdBandEnd}.", "Give a module pack's built-in an id from 100 to 499 and a distribution's one from 500 to 999.");
                problems = true;
            }
            else if (item.Id <= CoreBandEnd && owner.Name != CoreFeature.FeatureName)
            {
                Report(context, $"{described} uses an id from 1 to {CoreBandEnd}, which are reserved for the platform's own built-ins.", "Give a module pack's built-in an id from 100 to 499 and a distribution's one from 500 to 999.");
                problems = true;
            }

            // --- The handler key's grammar ---
            if (!JobHandlerKeys.IsValid(item.HandlerKey))
            {
                Report(
                    context,
                    $"{described} names the handler key '{item.HandlerKey}', which is not two or more dot-separated segments of lowercase letters, digits and hyphens, each starting with a letter, of at most {JobLimits.MaxKeyLength} characters.",
                    "Name the key the handler's [JobHandler] attribute declares, such as 'acme.recompute-balances'.");
                problems = true;
            }

            // --- The cron expression's width; whether it parses is the startup check's ---
            if (string.IsNullOrWhiteSpace(item.Cron) || item.Cron.Length > JobLimits.MaxCronExpressionLength)
            {
                Report(
                    context,
                    $"{described} has no cron expression, or one longer than {JobLimits.MaxCronExpressionLength} characters.",
                    "Give a five-field Unix cron expression, such as '0 3 * * *' for three every morning.");
                problems = true;
            }

            // --- The zone, from the platform's set ---
            if (item.TimeZoneId is not null && !TimeZones.Contains(item.TimeZoneId))
            {
                string alias = QueryexTimeZones.Aliases.TryGetValue(item.TimeZoneId, out string? canonical)
                    ? $" It is an alias of '{canonical}'; name that instead."
                    : string.Empty;
                Report(
                    context,
                    $"{described} names the time zone '{item.TimeZoneId}', which is not in the platform's time-zone set.{alias}",
                    "Name a canonical IANA zone id, such as 'Asia/Kolkata', or none for the tenant's zone.");
                problems = true;
            }

            // --- The arguments, serialised as each fired job receives them ---
            string? argumentsJson = null;
            if (item.Arguments is not null)
            {
                try
                {
                    argumentsJson = JsonSerializer.Serialize(item.Arguments, TellmaJson.Options);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
                {
                    Report(context, $"{described} has arguments that cannot be serialised as JSON: {exception.Message}", "Pass arguments of a plain record or dictionary type.");
                    problems = true;
                }

                if (argumentsJson is not null && Encoding.UTF8.GetByteCount(argumentsJson) > JobLimits.MaxHandlerArgumentsBytes)
                {
                    Report(
                        context,
                        string.Create(CultureInfo.InvariantCulture, $"{described} has arguments of more than {JobLimits.MaxHandlerArgumentsBytes} bytes once serialised."),
                        "Pass smaller arguments; the handler can read larger data from the database.");
                    problems = true;
                }
            }

            if (problems)
            {
                return;
            }

            context.Record.BuiltInSchedules.Add(new ContributedBuiltInSchedule(
                new BuiltInScheduleSeed(item.Id, item.HandlerKey, item.Cron, argumentsJson, item.TimeZoneId),
                owner));
        }

        /// <summary>Reports one problem with a built-in schedule.</summary>
        /// <param name="context">The realisation.</param>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">How to fix it.</param>
        private static void Report(RealizationContext context, string problem, string fix)
        {
            context.Report(new CompositionProblem(CompositionNames.Source, problem, fix));
        }
    }
}
