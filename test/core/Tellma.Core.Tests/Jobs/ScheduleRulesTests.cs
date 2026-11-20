// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Infrastructure;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The schedule rules with no database: the policies a save pairs, the arguments' size in
    ///     UTF-8 bytes, the expression's dialect, the handler a user schedule names, what an update of
    ///     another user's schedule and of a built-in may change, and what a take-over and an
    ///     activation refuse.
    /// </summary>
    public sealed class ScheduleRulesTests : IDisposable
    {
        /// <summary>The user the saves and actions run as.</summary>
        private const int CallerId = 7;

        /// <summary>Another user, who owns the schedules the caller did not take over.</summary>
        private const int OwnerId = 8;

        /// <summary>A built-in's id, in the reserved band.</summary>
        private const int BuiltInId = 500;

        /// <summary>A user schedule's id, past the reserved band.</summary>
        private const int UserScheduleId = 1_001;

        /// <summary>A registered handler that allows user-created schedules.</summary>
        private const string Schedulable = "acme.recompute-balances";

        /// <summary>A registered handler that does not allow user-created schedules.</summary>
        private const string NotSchedulable = "acme.digest";

        /// <summary>A key a feature retired.</summary>
        private const string Retired = "acme.legacy";

        /// <summary>A key nothing registers.</summary>
        private const string Unknown = "acme.unknown";

        /// <summary>The composition: two handlers, one of them schedulable, and a retired key.</summary>
        private readonly ServiceProvider _provider = TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution
            .JobHandler<RecomputeHandler>()
            .JobHandler<DigestHandler>()
            .RetiredJobKey(Retired)))).BuildServiceProvider();

        /// <summary>The loader the contexts read through; no owner is inactive and no job is running.</summary>
        private readonly RulesLoader _loader = new();

        // --- The policies and the arguments ---

        [Fact]
        public async Task Replaying_every_missed_occurrence_requires_runs_that_may_overlap()
        {
            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(
                (New(MissedPolicy.ReplayAll, OverlapPolicy.Skip), null),
                (New(MissedPolicy.ReplayAll, OverlapPolicy.Allow), null),
                (New(MissedPolicy.Coalesce, OverlapPolicy.Skip), null),
                (New(MissedPolicy.Skip, OverlapPolicy.Skip), null));

            Assert.Equal([($"[0].{nameof(Schedule.MissedPolicy)}", ScheduleValidationCodes.ReplayAllRequiresAllow)], errors);
        }

        [Fact]
        public async Task The_arguments_are_held_to_what_a_job_carries_counted_in_utf8_bytes_not_characters()
        {
            // Two-byte characters filling the limit fit, and one more does not, though either is about
            // half the limit in characters.
            int half = JobLimits.MaxHandlerArgumentsBytes / 2;
            Schedule ascii = New(arguments: Json(new string('a', JobLimits.MaxHandlerArgumentsBytes - 2)));
            Schedule fits = New(arguments: Json(new string('é', half - 1)));
            Schedule over = New(arguments: Json(new string('é', half)));

            SaveContext<Schedule> context = Save(_loader, CallerId, (ascii, null), (fits, null), (over, null));
            await Rules().ValidateAsync(context);

            ValidationError error = Assert.Single(context.Errors.Items);
            Assert.Equal(($"[2].{nameof(Schedule.ArgumentsJson)}", ScheduleValidationCodes.ArgumentsTooLarge), (error.Path.ToString(), error.Code));
            Assert.Equal<object?>(JobLimits.MaxHandlerArgumentsBytes, error.Arguments["maxBytes"]);
        }

        // --- The expression and the handler ---

        [Fact]
        public async Task An_expression_outside_five_field_unix_cron_is_refused_as_invalid()
        {
            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(
                (New(cron: "every minute"), null),
                (New(cron: "0 0 * * * *"), null),
                (New(cron: "61 * * * *"), null),
                (New(cron: "*/5 * * * 1-5"), null));

            Assert.Equal(
                [
                    ($"[0].{nameof(Schedule.CronExpression)}", ScheduleValidationCodes.CronInvalid),
                    ($"[1].{nameof(Schedule.CronExpression)}", ScheduleValidationCodes.CronInvalid),
                    ($"[2].{nameof(Schedule.CronExpression)}", ScheduleValidationCodes.CronInvalid),
                ],
                errors);
        }

        [Fact]
        public async Task A_user_schedule_names_a_registered_schedulable_handler_and_a_retired_key_is_unknown()
        {
            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(
                (New(key: Unknown), null),
                (New(key: Retired), null),
                (New(key: NotSchedulable), null),
                (New(key: Schedulable), null));

            Assert.Equal(
                [
                    ($"[0].{nameof(Schedule.HandlerKey)}", ScheduleValidationCodes.HandlerUnknown),
                    ($"[1].{nameof(Schedule.HandlerKey)}", ScheduleValidationCodes.HandlerUnknown),
                    ($"[2].{nameof(Schedule.HandlerKey)}", ScheduleValidationCodes.HandlerNotSchedulable),
                ],
                errors);
        }

        // --- The updates ---

        [Fact]
        public async Task An_update_of_another_users_schedule_may_rename_it_and_change_its_policies_but_not_what_it_runs_or_when()
        {
            Schedule before = Stored(UserScheduleId, OwnerId);

            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(
                (Changed(before, static row =>
                    {
                        row.Name = "Renamed";
                        row.OverlapPolicy = OverlapPolicy.Allow;
                        row.CatchUpWindowMinutes = 60;
                    }), before),
                (Changed(before, static row => row.ArgumentsJson = "{}"), before),
                (Changed(before, static row => row.CronExpression = "0 4 * * *"), before),
                (Changed(before, static row => row.TimeZoneId = "Etc/UTC"), before),
                (Changed(before, static row => row.HandlerKey = NotSchedulable), before));

            Assert.Equal(
                [
                    ($"[1].{nameof(Schedule.ArgumentsJson)}", ScheduleValidationCodes.OwnedByAnotherUser),
                    ($"[2].{nameof(Schedule.CronExpression)}", ScheduleValidationCodes.OwnedByAnotherUser),
                    ($"[3].{nameof(Schedule.TimeZoneId)}", ScheduleValidationCodes.OwnedByAnotherUser),
                    ($"[4].{nameof(Schedule.HandlerKey)}", ScheduleValidationCodes.HandlerNotSchedulable),
                    ($"[4].{nameof(Schedule.HandlerKey)}", ScheduleValidationCodes.OwnedByAnotherUser),
                ],
                errors);
        }

        [Fact]
        public async Task An_update_of_the_callers_own_schedule_may_change_what_it_runs()
        {
            Schedule before = Stored(UserScheduleId, CallerId);

            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(
                (Changed(before, static row =>
                    {
                        row.ArgumentsJson = "{}";
                        row.CronExpression = "0 4 * * *";
                        row.TimeZoneId = "Etc/UTC";
                    }), before));

            Assert.Empty(errors);
        }

        [Fact]
        public async Task An_update_of_a_built_in_may_change_its_names_expression_and_zone_and_nothing_else_whatever_its_key()
        {
            // The built-in's key is registered by no handler here, as when another host runs it.
            Schedule before = Stored(BuiltInId, WellKnownIds.SystemUserId, key: Unknown);

            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(
                (Changed(before, static row =>
                    {
                        row.Name = "Renamed";
                        row.Name2 = "Second";
                        row.CronExpression = "0 4 * * *";
                        row.TimeZoneId = "Etc/UTC";
                    }), before),
                (Changed(before, static row => row.Code = "other"), before),
                (Changed(before, static row => row.HandlerKey = Schedulable), before),
                (Changed(before, static row => row.ArgumentsJson = "{}"), before),
                (Changed(before, static row => row.MissedPolicy = MissedPolicy.Skip), before),
                (Changed(before, static row => row.OverlapPolicy = OverlapPolicy.Allow), before),
                (Changed(before, static row => row.CatchUpWindowMinutes = 60), before));

            Assert.Equal(
                [
                    ($"[1].{nameof(Schedule.Code)}", ScheduleValidationCodes.BuiltInImmutable),
                    ($"[2].{nameof(Schedule.HandlerKey)}", ScheduleValidationCodes.BuiltInImmutable),
                    ($"[3].{nameof(Schedule.ArgumentsJson)}", ScheduleValidationCodes.BuiltInImmutable),
                    ($"[4].{nameof(Schedule.MissedPolicy)}", ScheduleValidationCodes.BuiltInImmutable),
                    ($"[5].{nameof(Schedule.OverlapPolicy)}", ScheduleValidationCodes.BuiltInImmutable),
                    ($"[6].{nameof(Schedule.CatchUpWindowMinutes)}", ScheduleValidationCodes.BuiltInImmutable),
                ],
                errors);
        }

        // --- The actions ---

        [Fact]
        public async Task A_take_over_of_a_built_in_is_refused_and_of_a_user_schedule_allowed()
        {
            IReadOnlyList<(string Path, string Code)> errors = await ActionAsync(
                ScheduleService<Schedule>.TakeOverAction,
                Stored(BuiltInId, WellKnownIds.SystemUserId),
                Stored(UserScheduleId, OwnerId));

            Assert.Equal([("Ids[0]", ScheduleValidationCodes.BuiltInImmutable)], errors);
        }

        [Fact]
        public async Task An_activation_refuses_an_expression_that_never_occurs_again_and_a_user_schedules_unknown_or_retired_key()
        {
            IReadOnlyList<(string Path, string Code)> errors = await ActionAsync(
                "activate",
                Stored(UserScheduleId, CallerId, cron: "0 0 30 2 *"),
                Stored(UserScheduleId + 1, CallerId, key: Unknown),
                Stored(UserScheduleId + 2, CallerId, key: Retired),
                Stored(BuiltInId, WellKnownIds.SystemUserId, key: Unknown),
                Stored(UserScheduleId + 3, CallerId));

            Assert.Equal(
                [
                    ("Ids[0]", ScheduleValidationCodes.CronNeverOccurs),
                    ("Ids[1]", ScheduleValidationCodes.HandlerUnknown),
                    ("Ids[2]", ScheduleValidationCodes.HandlerUnknown),
                ],
                errors);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
        }

        // --- Helpers ---

        /// <summary>The rules over the composition's handlers and a time-zone set of two zones.</summary>
        /// <returns>The rules.</returns>
        private ScheduleRules<Schedule> Rules()
        {
            return new ScheduleRules<Schedule>(_provider.GetRequiredService<JobHandlerRegistry>(), new TwoZones());
        }

        /// <summary>Validates a save.</summary>
        /// <param name="rows">The payload, each with its before image; null for a new row.</param>
        /// <returns>The errors, as paths and codes, ordered.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> SaveAsync(params (Schedule Row, Schedule? Before)[] rows)
        {
            SaveContext<Schedule> context = Save(_loader, CallerId, rows);
            await Rules().ValidateAsync(context);
            return [.. Errors(context.Errors).Order()];
        }

        /// <summary>Validates an action over loaded targets, asked in their order.</summary>
        /// <param name="action">The action.</param>
        /// <param name="targets">The targets.</param>
        /// <returns>The errors, as paths and codes, ordered.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> ActionAsync(string action, params Schedule[] targets)
        {
            ActionContext<Schedule, int> context = Action(_loader, CallerId, [.. targets.Select(static target => target.Id)], targets);
            await Rules().ValidateActionAsync(action, context);
            return [.. Errors(context.Errors).Order()];
        }

        /// <summary>A new user schedule of the schedulable handler.</summary>
        /// <param name="missed">Its missed policy.</param>
        /// <param name="overlap">Its overlap policy.</param>
        /// <param name="cron">Its expression.</param>
        /// <param name="key">Its handler key.</param>
        /// <param name="arguments">Its arguments; null for none.</param>
        /// <returns>The schedule.</returns>
        private static Schedule New(
            MissedPolicy missed = MissedPolicy.Coalesce,
            OverlapPolicy overlap = OverlapPolicy.Skip,
            string cron = "0 3 * * *",
            string key = Schedulable,
            string? arguments = null)
        {
            return new Schedule { Name = "New", HandlerKey = key, CronExpression = cron, MissedPolicy = missed, OverlapPolicy = overlap, ArgumentsJson = arguments };
        }

        /// <summary>A stored schedule, as its before image or an action's target.</summary>
        /// <param name="id">Its id.</param>
        /// <param name="owner">The user it runs as.</param>
        /// <param name="key">Its handler key.</param>
        /// <param name="cron">Its expression.</param>
        /// <returns>The schedule.</returns>
        private static Schedule Stored(int id, int owner, string key = Schedulable, string cron = "0 3 * * *")
        {
            return new Schedule
            {
                Id = id,
                Code = "stored",
                Name = "Stored",
                HandlerKey = key,
                CronExpression = cron,
                TimeZoneId = "Asia/Riyadh",
                RunAsUserId = owner,
                IsActive = false,
            };
        }

        /// <summary>A copy of a stored schedule with a change.</summary>
        /// <param name="before">The stored schedule.</param>
        /// <param name="change">The change.</param>
        /// <returns>The copy.</returns>
        private static Schedule Changed(Schedule before, Action<Schedule> change)
        {
            Schedule row = new()
            {
                Id = before.Id,
                Code = before.Code,
                Name = before.Name,
                HandlerKey = before.HandlerKey,
                ArgumentsJson = before.ArgumentsJson,
                CronExpression = before.CronExpression,
                TimeZoneId = before.TimeZoneId,
                MissedPolicy = before.MissedPolicy,
                OverlapPolicy = before.OverlapPolicy,
                CatchUpWindowMinutes = before.CatchUpWindowMinutes,
                RunAsUserId = before.RunAsUserId,
                IsActive = before.IsActive,
            };
            change(row);
            return row;
        }

        /// <summary>A JSON string of a text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The JSON.</returns>
        private static string Json(string text)
        {
            return "\"" + text + "\"";
        }

        /// <summary>Value sets holding only a time-zone set of two zones.</summary>
        private sealed class TwoZones : IValueSetRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<ValueSetDescriptor> All { get; } =
                [new ValueSetDescriptor(CoreValueSets.TimeZone, [new ValueSetEntry("Asia/Riyadh", null, "Asia/Riyadh"), new ValueSetEntry("Etc/UTC", null, "Etc/UTC")])];

            /// <inheritdoc />
            public ValueSetDescriptor? Find(string name)
            {
                return All.FirstOrDefault(set => string.Equals(set.Name, name, StringComparison.Ordinal));
            }
        }
    }
}
