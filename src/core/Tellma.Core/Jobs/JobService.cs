// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §11.1: the jobs' stack serves reads and four actions. Each action runs on the
    // pipeline's general action path: the first round loads the targets under the caller's read
    // filter and counts them under the action's grant, and the hook judges each target's state;
    // the method then appends the action's fixed statement over the loaded ids to the persist
    // batch. The statements repeat their preconditions, so a client's retry after a commit fails
    // the precondition rather than acting twice. Every action tells each target's requester the
    // job changed; resume and cancel withdraw the notices of a hold they end, confirm and the
    // cancel of a job waiting for confirmation withdraw its notice, and the actions that make a
    // job due now wake this instance's poller once they commit.
    /// <summary>
    ///     The jobs' stack service: the tenant's jobs read by query and details, and the
    ///     <c>retry</c>, <c>cancel</c>, <c>resume</c> and <c>confirm</c> actions.
    /// </summary>
    /// <remarks>
    ///     Every member reads, cancels and confirms the jobs they requested without a role; the
    ///     other jobs and the other actions take a grant.
    /// </remarks>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="jobs">The scope's job queue, which wakes this instance's poller after an action that makes jobs due.</param>
    /// <param name="publisher">The scope's client event publisher, which tells each requester their job changed.</param>
    public class JobService(IEntityPipelineFactory factory, IJobQueue jobs, IClientEventPublisher publisher) : EntityService<Job>(factory)
    {
        /// <summary>The action that runs a failed job again.</summary>
        public const string RetryAction = "retry";

        /// <summary>The action that stops a job that has not ended.</summary>
        public const string CancelAction = "cancel";

        /// <summary>The action that lets a paused job run.</summary>
        public const string ResumeAction = "resume";

        /// <summary>The action that confirms the warnings a job waits on.</summary>
        public const string ConfirmAction = "confirm";

        /// <summary>The securable action of <see cref="ConfirmAction" />, which the jobs' stack declares.</summary>
        public const string Confirm = "Confirm";

        /// <summary>The property of an action's arguments, under which an argument's error sits.</summary>
        private const string ArgumentsProperty = "Arguments";

        /// <summary>The most recipients one withdrawal of the paused notice names.</summary>
        private const int RecipientsPerWithdrawal = 1_000;

        /// <summary>Makes each failed target pending again, due now, with its attempts and error cleared.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the statement is composed.</returns>
        [EntityAction(RetryAction, Action = AccessActions.Retry, Description = "Runs each failed job again, from its first attempt.")]
        public async Task RetryAsync(ActionContext<Job, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            int[] targets = Targets(context);
            Append(context.Batch, data => JobStatements.Retry(data, targets));
            Changed(context);
            Queue().Nudge(context.Batch);
        }

        /// <summary>
        ///     Stops each target that has not ended: a running job is flagged and stops at its next
        ///     renewal, a job with a checkpoint or waiting for confirmation is queued flagged so its
        ///     next claim records the stop, and any other is cancelled at once.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the statement is composed.</returns>
        [EntityAction(CancelAction, Action = AccessActions.Cancel, Destructive = true, AllowWhenReadOnly = true, Description = "Stops each job that has not ended.")]
        public async Task CancelAsync(ActionContext<Job, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The held rows' requesters beyond the targets, with the targets in the first round ---
            ContextRef<RawResult>? heldElsewhere = HeldElsewhere(context);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            int[] targets = Targets(context);
            Append(context.Batch, data => JobStatements.Cancel(data, targets));
            WithdrawHeld(context, heldElsewhere);

            // --- A job waiting for confirmation waits no longer ---
            foreach (Job job in context.Entities.Where(static job => job.Status == JobStatus.PendingConfirmation))
            {
                context.Withdraw(new WithdrawRequest(JobNotices.PendingConfirmationKey(job.Id)));
            }

            Changed(context);
        }

        /// <summary>Makes each paused target pending again, due now, with its cancel flag and error cleared.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the statement is composed.</returns>
        [EntityAction(ResumeAction, Action = AccessActions.Resume, Description = "Lets each paused job run.")]
        public async Task ResumeAsync(ActionContext<Job, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The held rows' requesters beyond the targets, with the targets in the first round ---
            ContextRef<RawResult>? heldElsewhere = HeldElsewhere(context);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            int[] targets = Targets(context);
            Append(context.Batch, data => JobStatements.Resume(data, targets));
            WithdrawHeld(context, heldElsewhere);
            Changed(context);
            Queue().Nudge(context.Batch);
        }

        /// <summary>
        ///     Confirms the warnings each target waits on: the job's recorded warning keys join the keys
        ///     it runs with, and it is pending again, due now, from its first attempt.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The warning keys the caller confirms, which cover every key each target waits on.</param>
        /// <returns>A task that completes when the statement is composed.</returns>
        /// <exception cref="InvalidOperationException">The scope has no user to confirm for.</exception>
        [EntityAction(ConfirmAction, Action = Confirm, Mcp = McpExposure.Hidden, Description = "Confirms the warnings each job waits on, so it runs on.")]
        public async Task ConfirmAsync(ActionContext<Job, int> context, ConfirmJobArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            int caller = Caller(context.Context)
                ?? throw new InvalidOperationException("Confirming a job's warnings names the caller as its requester, and the scope has no user.");
            int[] targets = Targets(context);
            IReadOnlyList<string> keys = [.. arguments.Keys];
            Append(context.Batch, data => JobStatements.Confirm(data, targets, keys, caller));

            // --- Each target's notice: it waits no longer ---
            foreach (Job job in context.Entities)
            {
                context.Withdraw(new WithdrawRequest(JobNotices.PendingConfirmationKey(job.Id)));
            }

            Changed(context);
            Queue().Nudge(context.Batch);
        }

        // Spec 0020 §11.1's preconditions, judged on the targets the first round loaded: a refused
        // target fails the whole action, at the target's position among the ids.
        /// <summary>Judges each target's state against the action's precondition, and the confirmation's keys.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when every target is judged.</returns>
        protected override async Task ValidateActionAsync(string action, ActionContext<Job, int> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateActionAsync(action, context).ConfigureAwait(false);

            // --- The confirmation's keys: each named, and as short as a confirmation key is ---
            var confirmation = context.Arguments as ConfirmJobArguments;
            if (action == ConfirmAction && confirmation is not null)
            {
                CheckKeys(context.Errors, confirmation.Keys);
            }

            await context.Loader.LoadAsync().ConfigureAwait(false);

            // --- Each target's state ---
            int? caller = Caller(context.Context);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                Job job = context.Entities[index];
                string? refusal = action switch
                {
                    RetryAction when job.Status != JobStatus.Failed => JobValidationCodes.NotRetryable,
                    CancelAction when job.Status is not (JobStatus.Pending or JobStatus.Held or JobStatus.Running or JobStatus.PendingConfirmation) => JobValidationCodes.NotCancellable,
                    ResumeAction when job.Status != JobStatus.Held => JobValidationCodes.NotHeld,
                    ConfirmAction => ConfirmRefusal(job, caller, confirmation),
                    _ => null,
                };

                if (refusal is not null)
                {
                    context.Errors.Add(AccessRefusals.Target(context.Ids, job.Id, index), refusal);
                }
            }
        }

        /// <summary>Why a confirmation is refused on one target: it waits on nothing, the caller did not start it, or the keys leave out one it waits on.</summary>
        /// <param name="job">The target.</param>
        /// <param name="caller">The caller; null for a scope without a user.</param>
        /// <param name="confirmation">The confirmation's arguments; null when the pipeline passed none.</param>
        /// <returns>The code, or null when the target can be confirmed.</returns>
        private static string? ConfirmRefusal(Job job, int? caller, ConfirmJobArguments? confirmation)
        {
            if (job.Status != JobStatus.PendingConfirmation || job.PendingConfirmationsJson is not string record)
            {
                return JobValidationCodes.NotPendingConfirmation;
            }

            if (caller is null || job.RequestedById != caller)
            {
                return JobValidationCodes.NotInitiator;
            }

            // --- Every key the job waits on is among the keys confirmed ---
            HashSet<string> confirmed = new(confirmation?.Keys.OfType<string>() ?? [], StringComparer.Ordinal);
            return PendingConfirmations.Keys(record).All(confirmed.Contains) ? null : JobValidationCodes.NotPendingConfirmation;
        }

        /// <summary>Refuses a confirmation key that is null, empty or longer than a confirmation key may be.</summary>
        /// <param name="errors">The action's errors.</param>
        /// <param name="keys">The keys.</param>
        private static void CheckKeys(ValidationErrors<Job> errors, IReadOnlyList<string> keys)
        {
            // The list's own presence and count are its validation attributes'; each key is judged here.
            if (keys is null)
            {
                return;
            }

            ValidationPath list = ValidationPath.Root.Property(ArgumentsProperty).Property(nameof(ConfirmJobArguments.Keys));
            for (int index = 0; index < keys.Count; index++)
            {
                string? key = keys[index];
                if (string.IsNullOrWhiteSpace(key))
                {
                    errors.Add(list.Index(index), ValidationCodes.Required);
                }
                else if (key.Length > JobLimits.MaxConfirmationKeyLength)
                {
                    errors.Add(list.Index(index), ValidationCodes.MaxLength, new Dictionary<string, object?>(StringComparer.Ordinal) { ["maxLength"] = JobLimits.MaxConfirmationKeyLength });
                }
            }
        }

        // Spec 0020 §11.1: a load without a grant filter, beside the targets in the first round: the
        // distinct requesters, null among them, of the held rows outside the targets, from the
        // IX_Jobs_Held filtered index. The status is spelled out because an interpolated value here
        // would be bound as a parameter, which the index's filter could not match.
        /// <summary>Requests the requesters of the held jobs outside the targets.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The pending load: one result set of nullable user ids; null when the action has no targets.</returns>
        private static ContextRef<RawResult>? HeldElsewhere(ActionContext<Job, int> context)
        {
            if (context.Ids.Count == 0)
            {
                return null;
            }

            List<IdList> keys = [.. context.Ids.Distinct().Order().Select(static id => new IdList { Id = id })];
            return context.Loader.Sql(
                $"SELECT DISTINCT [j].[RequestedById] FROM [core].[Jobs] AS [j] WHERE [j].[Status] = 'Held' AND NOT EXISTS (SELECT 1 FROM {keys} AS [k] WHERE [k].[Id] = [j].[Id]);",
                new SqlOptions(resultSets: 1));
        }

        // Spec 0020 §11.1 and §10: when the action ends held rows, each of their requesters with no
        // held row left loses the paused notice, and when no held row is left at all the
        // administrators lose the gap notice. A cancel or a resume of no held row withdraws neither.
        // The decision is best effort across the two round trips.
        /// <summary>Withdraws the paused notices of the requesters whose last held jobs the action ends, and the gap notice when it ends the last held job.</summary>
        /// <param name="context">The action's context, its targets loaded.</param>
        /// <param name="heldElsewhere">The load of the held jobs' requesters outside the targets; null when the action has no targets.</param>
        private static void WithdrawHeld(ActionContext<Job, int> context, ContextRef<RawResult>? heldElsewhere)
        {
            List<Job> held = [.. context.Entities.Where(static job => job.Status == JobStatus.Held)];
            if (held.Count == 0 || heldElsewhere is null)
            {
                return;
            }

            // --- The requesters with a held job left, and whether any held job is left at all ---
            QueryRowSet set = heldElsewhere.Value.ResultSets[0];
            int[] buffer = (int[])set.GetBuffer(0);
            HashSet<int> stillHeld = [.. Enumerable.Range(0, set.RowCount).Where(row => !set.IsNull(0, row)).Select(row => buffer[row])];

            // --- Each requester whose last held job this is loses the paused notice ---
            int[] released = [.. held.Select(static job => job.RequestedById).OfType<int>().Distinct().Where(requester => !stillHeld.Contains(requester)).Order()];
            foreach (int[] recipients in released.Chunk(RecipientsPerWithdrawal))
            {
                context.Withdraw(new WithdrawRequest(JobNotices.HeldType, recipients));
            }

            // --- With no held job left, the gap notice goes from every recipient ---
            if (set.RowCount == 0)
            {
                context.Withdraw(new WithdrawRequest(JobNotices.GapType));
            }
        }

        /// <summary>Tells each target's requester the job changed, once the action commits.</summary>
        /// <param name="context">The action's context, its targets loaded.</param>
        private void Changed(ActionContext<Job, int> context)
        {
            foreach (Job job in context.Entities)
            {
                if (job.RequestedById is int requester)
                {
                    publisher.Publish(context.Batch, JobNotices.Changed(job.Id, requester));
                }
            }
        }

        /// <summary>The ids of the targets the first round loaded, in load order.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The ids.</returns>
        private static int[] Targets(ActionContext<Job, int> context)
        {
            return [.. context.Entities.Select(static job => job.Id)];
        }

        /// <summary>The user a scope runs as: the system user in a system scope, else its user.</summary>
        /// <param name="context">The scope's request context.</param>
        /// <returns>The user; null for a scope with neither.</returns>
        private static int? Caller(RequestContext context)
        {
            return context.IsSystem ? WellKnownIds.SystemUserId : context.UserId;
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
                ?? throw new ArgumentException("The batch is not one a tenant database created, so the jobs' statements cannot ride it.", nameof(batch)));
        }

        /// <summary>The scope's job queue, as the platform composes it.</summary>
        /// <returns>The queue.</returns>
        /// <exception cref="InvalidOperationException">The scope's queue is not the platform's.</exception>
        private JobQueue Queue()
        {
            return jobs as JobQueue
                ?? throw new InvalidOperationException("The scope's job queue is not the platform's, so it cannot wake the poller after the action.");
        }
    }

    /// <summary>The arguments of the jobs' <c>confirm</c> action.</summary>
    /// <param name="Keys">The warning keys confirmed: one to sixteen, each of at most a hundred characters, covering every key each target waits on.</param>
    public sealed record ConfirmJobArguments([property: Required, Length(1, JobLimits.MaxConfirmationKeys)] IReadOnlyList<string> Keys);
}
