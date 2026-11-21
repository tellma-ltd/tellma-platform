// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Batches;
using Cron = Cronos.CronExpression;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §7.8: the schedules' service. Its save and activation hook keeps core.ScheduleStates
    // in step (a next due time from the application clock, which the next tick corrects against the
    // database's), its details extra gives a schedule's state, its record rule tells the client what
    // a built-in refuses, and two actions take a schedule over or run it now. The rules every
    // operation checks are the stack's ScheduleRules component, so a distribution's leaf keeps them.
    /// <summary>
    ///     The schedules' stack service: the standard operations over the tenant's schedules, the
    ///     scheduler's state kept in step with each save and activation, the <c>state</c> details
    ///     extra, and the <c>take-over</c> and <c>run-now</c> actions; closed over the distribution's
    ///     schedule leaf.
    /// </summary>
    /// <remarks>
    ///     A built-in schedule (its id in the reserved band) runs as the platform: a tenant changes its
    ///     names, expression and zone, nothing else, never deletes or deactivates it, and never takes
    ///     it over. A user schedule runs as its creator until a user takes it over.
    /// </remarks>
    /// <typeparam name="TSchedule">The stack's schedule leaf.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="jobs">The scope's job queue, which the run-now action inserts its job through.</param>
    /// <param name="notifier">The scope's notifier, which a delete withdraws the paused notices through.</param>
    [DetailsExtras(StateExtra)]
    public class ScheduleService<TSchedule>(IEntityPipelineFactory factory, IJobQueue jobs, INotifier notifier) : EntityService<TSchedule>(factory)
        where TSchedule : Schedule
    {
        /// <summary>The details extra of a schedule's state: its next due time and what it last fired.</summary>
        public const string StateExtra = "state";

        /// <summary>The action that makes the caller the user a schedule runs as.</summary>
        public const string TakeOverAction = "take-over";

        /// <summary>The action that runs a schedule's job once, now.</summary>
        public const string RunNowAction = "run-now";

        /// <summary>The ability the record rule reads for a delete.</summary>
        private const string DeleteAbility = "delete";

        /// <summary>The ability the record rule reads for an activation or a deactivation.</summary>
        private const string ActivateAbility = "activate";

        /// <summary>The built-in action that activates a schedule.</summary>
        private const string Activate = "activate";

        /// <summary>The built-in action that deactivates a schedule.</summary>
        private const string Deactivate = "deactivate";

        /// <summary>The most withdrawals one batch carries after a delete.</summary>
        private const int WithdrawalsPerBatch = 100;

        /// <summary>
        ///     Makes the caller the user each target runs as, the explicit way to run another user's
        ///     schedule as oneself. It changes the owner alone: a paused schedule stays paused.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the update is composed.</returns>
        /// <exception cref="InvalidOperationException">The scope has no user to make the owner.</exception>
        [EntityAction(TakeOverAction, Action = AccessActions.Save, Description = "Makes the caller the user each schedule's jobs run as.")]
        public Task TakeOverAsync(ActionContext<TSchedule, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            int caller = ScheduleRules<TSchedule>.Caller(context.Context)
                ?? throw new InvalidOperationException("Taking a schedule over makes the caller its owner, and the scope has no user.");
            context.Update(new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(Schedule.RunAsUserId)] = caller });
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Runs the schedule's job once, now: as the caller for a user schedule and as the platform
        ///     for a built-in, with the caller as its requester. The schedule's own state is left alone,
        ///     and a paused or inactive schedule can be run too.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The job, once the action committed.</returns>
        /// <exception cref="InvalidOperationException">The scope has no user to run the job for.</exception>
        [EntityAction(RunNowAction, Action = AccessActions.Save, SingleTarget = true, Description = "Runs the schedule's job once, now.")]
        public async Task<JobAccepted> RunNowAsync(ActionContext<TSchedule, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The target, loaded under the grant; a missing one fails the action on its own ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            if (context.Entities is not [TSchedule schedule])
            {
                await context.Persisted.ConfigureAwait(false);
                throw new InvalidOperationException("The run-now action committed without its one target.");
            }

            // --- The job: the platform runs a built-in's; the caller asked for it either way ---
            int caller = ScheduleRules<TSchedule>.Caller(context.Context)
                ?? throw new InvalidOperationException("Running a schedule now records who asked, and the scope has no user.");
            int runAs = ScheduleRules<TSchedule>.IsBuiltIn(schedule.Id) ? WellKnownIds.SystemUserId : caller;
            BatchResult<int> job = Queue().RunNow(context.Batch, schedule.Id, runAs, caller);
            await context.Persisted.ConfigureAwait(false);
            return new JobAccepted(job.Value, ResourceId: null);
        }

        /// <summary>Preprocesses a save: a new schedule runs as its creator and is not paused.</summary>
        /// <param name="context">The save's context; its rows have no final ids yet.</param>
        /// <returns>A task that completes when the payload is preprocessed.</returns>
        protected override async Task PreprocessAsync(SaveContext<TSchedule> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.PreprocessAsync(context).ConfigureAwait(false);

            // The platform already trimmed the text and reset the server-owned values; these are the
            // values a new row keeps.
            int? caller = ScheduleRules<TSchedule>.Caller(context.Context);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                if (context.IsNew(index))
                {
                    TSchedule row = context.Entities[index];
                    row.RunAsUserId = caller ?? throw new InvalidOperationException("A new schedule runs as its creator, and the scope has no user.");
                    row.PausedReason = null;
                }
            }
        }

        // Spec 0020 §7.8: a save recomputes the next due time of each new schedule and of each one
        // whose expression or zone changed (a rename leaves a due occurrence due); an activation the
        // next due time of each schedule it brings back, from now, so a paused period is never caught
        // up, and clears the paused reason and withdraws its notice; a deactivation nulls it. The
        // upsert releases any tick's lease of the row, so a tick that read the schedule before the
        // change fails its fence instead of advancing it from what it read; every other updated row and
        // every take-over target has its lease released alone, so a tick never fires the arguments,
        // policies or owner it read before the change.
        /// <summary>
        ///     Appends the schedules' state upsert, the release of a tick's lease of every other updated
        ///     or taken-over schedule, on an activation the paused reasons' clear and their notices'
        ///     withdrawal, and on a delete the declaration of what its keys wrote.
        /// </summary>
        /// <param name="context">The persist context, of the operation's kind.</param>
        /// <returns>A task that completes when the statements are appended.</returns>
        protected override async Task ContributeAsync(PersistContext<TSchedule> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ContributeAsync(context).ConfigureAwait(false);

            TimeZoneInfo tenantZone = ScheduleCron.TenantZone(context.Context);
            DateTimeOffset now = context.Context.Now;
            List<ScheduleNextList> states = [];
            List<int> released = [];
            switch (context)
            {
                // --- A save: new schedules, and those whose expression or zone changed ---
                case SavePersistContext<TSchedule> save:
                    for (int index = 0; index < save.Entities.Count; index++)
                    {
                        TSchedule row = save.Entities[index];
                        if (save.Before(index) is not TSchedule before || before.CronExpression != row.CronExpression || before.TimeZoneId != row.TimeZoneId)
                        {
                            states.Add(new ScheduleNextList { ScheduleId = row.Id, NextDueAt = row.IsActive ? Next(row, now, tenantZone) : null });
                        }
                        else
                        {
                            released.Add(row.Id);
                        }
                    }

                    break;

                // --- An activation: the schedules it brings back, their paused reasons and notices ---
                case ActionPersistContext<TSchedule> { Action: Activate } activation:
                    List<int> paused = [];
                    foreach (TSchedule row in activation.Entities.Where(static row => !row.IsActive))
                    {
                        states.Add(new ScheduleNextList { ScheduleId = row.Id, NextDueAt = Next(row, now, tenantZone) });
                        if (row.PausedReason is not null)
                        {
                            paused.Add(row.Id);
                        }
                    }

                    if (paused.Count > 0)
                    {
                        Append(activation.Batch, data => JobStatements.ClearPausedReason(data, paused));
                        foreach (int id in paused)
                        {
                            activation.Withdraw(new WithdrawRequest(JobNotices.SchedulePausedKey(id)));
                        }
                    }

                    break;

                // --- A take-over: a tick in flight must not fire as the previous owner ---
                case ActionPersistContext<TSchedule> { Action: TakeOverAction } takeOver:
                    released.AddRange(takeOver.Entities.Select(static row => row.Id));
                    break;

                // --- A deactivation: nothing is scheduled for the schedules it stops ---
                case ActionPersistContext<TSchedule> { Action: Deactivate } deactivation:
                    states.AddRange(deactivation.Entities.Where(static row => row.IsActive).Select(static row => new ScheduleNextList { ScheduleId = row.Id, NextDueAt = null }));
                    break;

                // --- A delete: the states leave with their schedules by their key's cascade and the jobs
                // lose their schedule by theirs; this statement declares both among the batch's writes,
                // so its change tracking and tags see them ---
                case DeletePersistContext<TSchedule> deletion:
                    SqlIdentifier deleted = deletion.AffectedIds;
                    Append(deletion.Batch, data => JobStatements.DeclareScheduleDelete(data, deleted));
                    break;

                default:
                    break;
            }

            if (states.Count > 0)
            {
                Append(context.Batch, data => JobStatements.UpsertScheduleStates(data, states));
            }

            if (released.Count > 0)
            {
                Append(context.Batch, data => JobStatements.ReleaseTickLeases(data, released));
            }
        }

        /// <summary>Withdraws the paused notice of every schedule a delete removed.</summary>
        /// <param name="outcome">The committed operation's outcome, of its kind.</param>
        /// <returns>A task that completes when the withdrawals were sent.</returns>
        protected override async Task AfterCommitAsync(PersistOutcome<TSchedule> outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            await base.AfterCommitAsync(outcome).ConfigureAwait(false);
            if (outcome is not DeletePersistOutcome<TSchedule, int> deleted)
            {
                return;
            }

            foreach (int[] chunk in deleted.DeletedIds.Chunk(WithdrawalsPerBatch))
            {
                await notifier.WithdrawAsync([.. chunk.Select(static id => new WithdrawRequest(JobNotices.SchedulePausedKey(id)))], CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>Adds the <c>state</c> extra when the read requests it: one statement over the visible ids, writing nothing.</summary>
        /// <param name="plan">The details plan.</param>
        protected override void ContributeDetails(DetailsPlan<TSchedule, int> plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            base.ContributeDetails(plan);
            if (plan.Request.Include.Contains(StateExtra, StringComparer.Ordinal))
            {
                plan.AddExtra(
                    StateExtra,
                    $"SELECT [st].[ScheduleId], [st].[NextDueAt], [st].[LastFiredAt], [st].[LastScheduledFor], [st].[LastSkippedAt], [st].[LastJobId] FROM [core].[ScheduleStates] AS [st] INNER JOIN {plan.IdsSource} AS [k] ON [k].[Id] = [st].[ScheduleId];",
                    new SqlOptions(resultSets: 1));
            }
        }

        /// <summary>What a built-in refuses: a delete and a take-over, which it is immutable to, and an activation change, since it is always active.</summary>
        /// <param name="row">The row.</param>
        /// <param name="ability">The ability.</param>
        /// <returns>The reason's resource key, or null.</returns>
        protected override string? RefuseOnRecord(TSchedule row, string ability)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(ability);
            return !ScheduleRules<TSchedule>.IsBuiltIn(row.Id) ? base.RefuseOnRecord(row, ability) : ability switch
            {
                DeleteAbility or "action:" + TakeOverAction => ScheduleValidationCodes.BuiltInImmutable,
                ActivateAbility => ScheduleValidationCodes.BuiltInAlwaysActive,
                _ => base.RefuseOnRecord(row, ability),
            };
        }

        /// <summary>The next due time of a schedule from an instant, in its zone, or the tenant's when it names none or one this host cannot resolve.</summary>
        /// <param name="row">The schedule.</param>
        /// <param name="now">The instant.</param>
        /// <param name="tenantZone">The tenant's zone.</param>
        /// <returns>The next due time; null for an expression that does not parse or never occurs again, which the rules refuse.</returns>
        private static DateTimeOffset? Next(TSchedule row, DateTimeOffset now, TimeZoneInfo tenantZone)
        {
            return ScheduleCron.TryParse(row.CronExpression, out Cron? cron)
                ? ScheduleCron.Next(cron, now, ScheduleCron.TryZone(row.TimeZoneId, tenantZone, out TimeZoneInfo? zone) ? zone : tenantZone)
                : null;
        }

        /// <summary>Appends a fixed statement on a tenant database's batch, or records it on a persist participant's view of one.</summary>
        /// <param name="batch">The batch, or a recording view of it.</param>
        /// <param name="append">Appends the statement.</param>
        /// <exception cref="ArgumentException">The batch is not one a tenant database created.</exception>
        private static void Append(IDataBatch batch, Action<DataBatch> append)
        {
            // A recorded append is replayed onto each composition of the persist batch.
            if (batch is RecordingBatch recording)
            {
                recording.Record(inner => Append(inner, append));
                return;
            }

            append(batch as DataBatch
                ?? throw new ArgumentException("The batch is not one a tenant database created, so the schedules' statements cannot ride it.", nameof(batch)));
        }

        /// <summary>The scope's job queue, as the platform composes it.</summary>
        /// <returns>The queue.</returns>
        /// <exception cref="InvalidOperationException">The scope's queue is not the platform's.</exception>
        private JobQueue Queue()
        {
            return jobs as JobQueue
                ?? throw new InvalidOperationException("The scope's job queue is not the platform's, so the run-now job cannot be inserted through it.");
        }
    }
}
