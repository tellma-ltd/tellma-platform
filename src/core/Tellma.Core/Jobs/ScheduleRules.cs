// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Cron = Cronos.CronExpression;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §7.1 and §7.8: the schedule rules, a component of the stack so a distribution's leaf
    // keeps them. A save checks the expression in the dialect the scheduler evaluates and that it
    // occurs again, the zone against the time-zone set (an alias is outside it), the handler (a user
    // schedule names a registered, schedulable one), the policies, the arguments' size, the owner
    // rule and what a built-in lets change. A delete and a take-over refuse built-ins; a deactivation
    // refuses a built-in; an activation refuses an inactive owner, an expression that never occurs
    // again and an unknown key; a run-now refuses a Skip schedule whose job has not ended.
    /// <summary>The rules of the schedules' stack: what a save, a delete and each action may do.</summary>
    /// <typeparam name="TSchedule">The stack's schedule leaf.</typeparam>
    internal sealed class ScheduleRules<TSchedule> : IEntityValidator<TSchedule>
        where TSchedule : Schedule
    {
        /// <summary>The built-in action that activates a schedule.</summary>
        private const string Activate = "activate";

        /// <summary>The built-in action that deactivates a schedule.</summary>
        private const string Deactivate = "deactivate";

        /// <summary>The handlers and retired keys this instance knows.</summary>
        private readonly JobHandlerRegistry _registry;

        /// <summary>The time-zone set's values, matched by their exact spelling.</summary>
        private readonly Lazy<FrozenSet<string>> _zones;

        /// <summary>Creates the rules.</summary>
        /// <param name="registry">The handlers and retired keys this instance knows.</param>
        /// <param name="valueSets">The value sets, whose time-zone set a schedule's zone must be in.</param>
        public ScheduleRules(JobHandlerRegistry registry, IValueSetRegistry valueSets)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(valueSets);
            _registry = registry;
            _zones = new(() => Zones(valueSets), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public async Task ValidateAsync(SaveContext<TSchedule> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The before images, with the first round ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            TimeZoneInfo tenantZone = ScheduleCron.TenantZone(context.Context);
            int? caller = Caller(context.Context);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TSchedule row = context.Entities[index];
                bool isNew = context.IsNew(index);
                bool builtIn = !isNew && IsBuiltIn(row.Id);
                CheckTiming(context, index, row, tenantZone);
                CheckHandler(context, index, row, builtIn);
                CheckPolicies(context, index, row);
                if (!isNew)
                {
                    CheckUpdate(context, index, row, builtIn, caller);
                }
            }
        }

        /// <inheritdoc />
        public Task ValidateDeleteAsync(DeleteContext<TSchedule> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // A built-in is refused by its id alone, so no row is loaded for the rule.
            IReadOnlyList<int> ids = (context as DeleteContext<TSchedule, int>)?.Ids ?? [];
            for (int index = 0; index < ids.Count; index++)
            {
                if (IsBuiltIn(ids[index]))
                {
                    context.Errors.Add(AccessRefusals.Target(ids, ids[index], index), ScheduleValidationCodes.BuiltInImmutable);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task ValidateActionAsync(string action, ActionContext<TSchedule> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);

            // --- What the action's rule reads, with the targets in the first round ---
            IReadOnlyList<int> ids = (context as ActionContext<TSchedule, int>)?.Ids ?? [];
            ContextRef<RawResult>? inactiveOwners = action == Activate && ids.Count > 0 ? InactiveOwners(context.Loader, ids) : null;
            ContextRef<RawResult>? running = action == ScheduleService<TSchedule>.RunNowAction && ids.Count > 0 ? Running(context.Loader, ids) : null;
            await context.Loader.LoadAsync().ConfigureAwait(false);

            // --- Each target's rule ---
            HashSet<int> ownerInactive = inactiveOwners is null ? [] : Ids(inactiveOwners.Value);
            HashSet<int> inProgress = running is null ? [] : Ids(running.Value);
            TimeZoneInfo tenantZone = ScheduleCron.TenantZone(context.Context);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TSchedule row = context.Entities[index];
                ValidationPath at = AccessRefusals.Target(ids, row.Id, index);
                switch (action)
                {
                    case Deactivate when IsBuiltIn(row.Id):
                        context.Errors.Add(at, ScheduleValidationCodes.BuiltInAlwaysActive);
                        break;

                    case Activate:
                        CheckActivation(context.Errors, at, row, ownerInactive.Contains(row.Id), context.Context.Now, tenantZone);
                        break;

                    case ScheduleService<TSchedule>.TakeOverAction when IsBuiltIn(row.Id):
                        context.Errors.Add(at, ScheduleValidationCodes.BuiltInImmutable);
                        break;

                    case ScheduleService<TSchedule>.RunNowAction when row.OverlapPolicy == OverlapPolicy.Skip && inProgress.Contains(row.Id):
                        context.Errors.Add(at, ScheduleValidationCodes.RunInProgress);
                        break;

                    default:
                        break;
                }
            }
        }

        /// <summary>Whether a schedule id lies in the reserved band, so the schedule is built in.</summary>
        /// <param name="id">The id.</param>
        /// <returns>True for a built-in.</returns>
        internal static bool IsBuiltIn(int id)
        {
            return id is > 0 and <= WellKnownIds.ReservedIdBandEnd;
        }

        /// <summary>The user a scope runs as: the system user in a system scope, else its user.</summary>
        /// <param name="context">The scope's request context.</param>
        /// <returns>The user; null for a scope with neither.</returns>
        internal static int? Caller(RequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.IsSystem ? WellKnownIds.SystemUserId : context.UserId;
        }

        /// <summary>
        ///     The expression parses and occurs again in the schedule's zone, and the zone is in the
        ///     time-zone set; the next occurrence is judged in the tenant's zone when the zone is refused.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <param name="index">The row.</param>
        /// <param name="row">The schedule.</param>
        /// <param name="tenantZone">The tenant's zone.</param>
        private void CheckTiming(SaveContext<TSchedule> context, int index, TSchedule row, TimeZoneInfo tenantZone)
        {
            // --- The zone: in the set as spelled; the save already matched its case to the set's ---
            bool zoneKnown = row.TimeZoneId is null || _zones.Value.Contains(row.TimeZoneId);
            if (!zoneKnown)
            {
                context.Errors.Add(index, static schedule => schedule.TimeZoneId, ScheduleValidationCodes.TimeZoneUnknown, Arguments(("timeZone", row.TimeZoneId)));
            }

            // --- The expression: the scheduler's dialect, and a next occurrence ---
            if (row.CronExpression is null)
            {
                return;
            }

            if (!ScheduleCron.TryParse(row.CronExpression, out Cron? cron))
            {
                context.Errors.Add(index, static schedule => schedule.CronExpression, ScheduleValidationCodes.CronInvalid, Arguments(("expression", row.CronExpression)));
                return;
            }

            TimeZoneInfo zone = zoneKnown && ScheduleCron.TryZone(row.TimeZoneId, tenantZone, out TimeZoneInfo? found) ? found : tenantZone;
            if (ScheduleCron.Next(cron, context.Context.Now, zone) is null)
            {
                context.Errors.Add(index, static schedule => schedule.CronExpression, ScheduleValidationCodes.CronNeverOccurs, Arguments(("expression", row.CronExpression)));
            }
        }

        // A built-in's key is fixed: it is write-once, and the startup checks report a built-in whose
        // key no handler registers, since another host may run it. So only a user schedule's key is
        // checked here.
        /// <summary>A user schedule names a registered handler that allows user-created schedules.</summary>
        /// <param name="context">The save's context.</param>
        /// <param name="index">The row.</param>
        /// <param name="row">The schedule.</param>
        /// <param name="builtIn">Whether the schedule is built in.</param>
        private void CheckHandler(SaveContext<TSchedule> context, int index, TSchedule row, bool builtIn)
        {
            if (builtIn || row.HandlerKey is null)
            {
                return;
            }

            if (!_registry.TryFind(row.HandlerKey, out JobHandlerRegistration? handler) || _registry.IsRetired(row.HandlerKey))
            {
                context.Errors.Add(index, static schedule => schedule.HandlerKey, ScheduleValidationCodes.HandlerUnknown, Arguments(("handlerKey", row.HandlerKey)));
            }
            else if (!handler.Options.Schedulable)
            {
                context.Errors.Add(index, static schedule => schedule.HandlerKey, ScheduleValidationCodes.HandlerNotSchedulable, Arguments(("handlerKey", row.HandlerKey)));
            }
        }

        /// <summary>Every missed occurrence is replayed only when runs may overlap, and the arguments fit a job.</summary>
        /// <param name="context">The save's context.</param>
        /// <param name="index">The row.</param>
        /// <param name="row">The schedule.</param>
        private static void CheckPolicies(SaveContext<TSchedule> context, int index, TSchedule row)
        {
            if (row.MissedPolicy == MissedPolicy.ReplayAll && row.OverlapPolicy == OverlapPolicy.Skip)
            {
                context.Errors.Add(index, static schedule => schedule.MissedPolicy, ScheduleValidationCodes.ReplayAllRequiresAllow);
            }

            // Each job copies the arguments, so they are held to what a job may carry.
            if (row.ArgumentsJson is string arguments && Encoding.UTF8.GetByteCount(arguments) > JobLimits.MaxHandlerArgumentsBytes)
            {
                context.Errors.Add(index, static schedule => schedule.ArgumentsJson, ScheduleValidationCodes.ArgumentsTooLarge, Arguments(("maxBytes", JobLimits.MaxHandlerArgumentsBytes)));
            }
        }

        /// <summary>
        ///     An update of a built-in changes only its names, expression and zone; an update of another
        ///     user's schedule leaves what it runs alone.
        /// </summary>
        /// <param name="context">The save's context, after its first round.</param>
        /// <param name="index">The row.</param>
        /// <param name="row">The schedule.</param>
        /// <param name="builtIn">Whether the schedule is built in.</param>
        /// <param name="caller">The user the save runs as.</param>
        private static void CheckUpdate(SaveContext<TSchedule> context, int index, TSchedule row, bool builtIn, int? caller)
        {
            // An update row always has its before image once the first round ran.
            if (context.Before(index) is not TSchedule before)
            {
                return;
            }

            if (builtIn)
            {
                // --- A built-in: anything but the names, the expression and the zone is fixed ---
                const string Fixed = ScheduleValidationCodes.BuiltInImmutable;
                Refuse(context, index, static schedule => schedule.Code, row.Code != before.Code, Fixed);
                Refuse(context, index, static schedule => schedule.HandlerKey, row.HandlerKey != before.HandlerKey, Fixed);
                Refuse(context, index, static schedule => schedule.ArgumentsJson, row.ArgumentsJson != before.ArgumentsJson, Fixed);
                Refuse(context, index, static schedule => schedule.MissedPolicy, row.MissedPolicy != before.MissedPolicy, Fixed);
                Refuse(context, index, static schedule => schedule.OverlapPolicy, row.OverlapPolicy != before.OverlapPolicy, Fixed);
                Refuse(context, index, static schedule => schedule.CatchUpWindowMinutes, row.CatchUpWindowMinutes != before.CatchUpWindowMinutes, Fixed);
                return;
            }

            // --- Another user's schedule: what it runs changes only once the caller took it over ---
            if (before.RunAsUserId != caller)
            {
                const string Owned = ScheduleValidationCodes.OwnedByAnotherUser;
                Refuse(context, index, static schedule => schedule.HandlerKey, row.HandlerKey != before.HandlerKey, Owned);
                Refuse(context, index, static schedule => schedule.ArgumentsJson, row.ArgumentsJson != before.ArgumentsJson, Owned);
                Refuse(context, index, static schedule => schedule.CronExpression, row.CronExpression != before.CronExpression, Owned);
                Refuse(context, index, static schedule => schedule.TimeZoneId, row.TimeZoneId != before.TimeZoneId, Owned);
            }
        }

        /// <summary>Refuses an update that changes a property, at the property.</summary>
        /// <typeparam name="TValue">The property's type.</typeparam>
        /// <param name="context">The save's context.</param>
        /// <param name="index">The row.</param>
        /// <param name="property">The property.</param>
        /// <param name="changed">Whether the update changes it.</param>
        /// <param name="code">The refusal's code.</param>
        private static void Refuse<TValue>(SaveContext<TSchedule> context, int index, Expression<Func<TSchedule, TValue>> property, bool changed, string code)
        {
            if (changed)
            {
                context.Errors.Add(index, property, code);
            }
        }

        /// <summary>
        ///     An activation stands only where its cause is gone: the owner is active, the expression
        ///     occurs again, and a user schedule's key is registered and not retired.
        /// </summary>
        /// <param name="errors">The action's errors.</param>
        /// <param name="at">The target's path.</param>
        /// <param name="row">The target as loaded.</param>
        /// <param name="ownerInactive">Whether the user it runs as is inactive.</param>
        /// <param name="now">The action's instant.</param>
        /// <param name="tenantZone">The tenant's zone.</param>
        private void CheckActivation(ValidationErrors<TSchedule> errors, ValidationPath at, TSchedule row, bool ownerInactive, DateTimeOffset now, TimeZoneInfo tenantZone)
        {
            if (ownerInactive)
            {
                errors.Add(at, ScheduleValidationCodes.OwnerInactive);
            }

            if (ScheduleCron.TryParse(row.CronExpression, out Cron? cron)
                && ScheduleCron.Next(cron, now, ScheduleCron.TryZone(row.TimeZoneId, tenantZone, out TimeZoneInfo? zone) ? zone : tenantZone) is null)
            {
                errors.Add(at, ScheduleValidationCodes.CronNeverOccurs, Arguments(("expression", row.CronExpression)));
            }

            if (!IsBuiltIn(row.Id) && (!_registry.TryFind(row.HandlerKey, out _) || _registry.IsRetired(row.HandlerKey)))
            {
                errors.Add(at, ScheduleValidationCodes.HandlerUnknown, Arguments(("handlerKey", row.HandlerKey)));
            }
        }

        /// <summary>Requests the targets whose run-as user is inactive.</summary>
        /// <param name="loader">The context loader.</param>
        /// <param name="ids">The target ids.</param>
        /// <returns>The pending load: one result set of schedule ids.</returns>
        private static ContextRef<RawResult> InactiveOwners(IContextLoader loader, IReadOnlyList<int> ids)
        {
            List<IdList> keys = [.. ids.Distinct().Order().Select(static id => new IdList { Id = id })];
            return loader.Sql(
                $"SELECT [s].[Id] FROM [core].[Schedules] AS [s] INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [s].[RunAsUserId] WHERE [s].[Id] IN (SELECT [k].[Id] FROM {keys} AS [k]) AND [u].[IsActive] = 0;",
                new SqlOptions(resultSets: 1));
        }

        // Spec 0020 §7.8: whether a job that has not ended carries the schedule's id, a seek on
        // IX_Jobs_ScheduleActive; the load reads the job table without the caller's grant on jobs.
        // The statuses are the active ones JobStatements names, spelled out because an interpolated
        // value here would be bound as a parameter.
        /// <summary>Requests the targets with a job that has not ended.</summary>
        /// <param name="loader">The context loader.</param>
        /// <param name="ids">The target ids.</param>
        /// <returns>The pending load: one result set of schedule ids.</returns>
        private static ContextRef<RawResult> Running(IContextLoader loader, IReadOnlyList<int> ids)
        {
            List<IdList> keys = [.. ids.Distinct().Order().Select(static id => new IdList { Id = id })];
            return loader.Sql(
                $"SELECT DISTINCT [j].[ScheduleId] FROM [core].[Jobs] AS [j] WHERE [j].[ScheduleId] IN (SELECT [k].[Id] FROM {keys} AS [k]) AND [j].[Status] IN ('Pending', 'Running', 'Held', 'PendingConfirmation');",
                new SqlOptions(resultSets: 1));
        }

        /// <summary>The ids a load returned in its one column.</summary>
        /// <param name="result">The load's result.</param>
        /// <returns>The ids.</returns>
        private static HashSet<int> Ids(RawResult result)
        {
            QueryRowSet set = result.ResultSets[0];
            int[] ids = (int[])set.GetBuffer(0);
            return [.. ids.Take(set.RowCount)];
        }

        /// <summary>An error's arguments.</summary>
        /// <param name="arguments">Each argument's name and value.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
        {
            return arguments.ToDictionary(static argument => argument.Name, static argument => argument.Value, StringComparer.Ordinal);
        }

        /// <summary>The time-zone set's values.</summary>
        /// <param name="valueSets">The value sets.</param>
        /// <returns>The values.</returns>
        /// <exception cref="InvalidOperationException">The platform's time-zone set is not composed.</exception>
        private static FrozenSet<string> Zones(IValueSetRegistry valueSets)
        {
            ValueSetDescriptor set = valueSets.Find(CoreValueSets.TimeZone)
                ?? throw new InvalidOperationException($"The value set {CoreValueSets.TimeZone}, which the platform composes, is missing.");
            return set.Values.Select(static entry => entry.Value).ToFrozenSet(StringComparer.Ordinal);
        }
    }
}
