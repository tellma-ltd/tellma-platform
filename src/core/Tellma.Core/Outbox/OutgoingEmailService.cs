// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §3.2 step 5 and §7.1: the outgoing emails' stack service. Rows arrive only as the
    // outbox's enlisted inserts, and each new row's sending job is enqueued in the same persist.
    // The retry and cancel actions follow spec 0020 §11.1's two-round-trip shape: the first round
    // loads the targets under the caller's read filter, counts them under the action's grant and,
    // beside them, reads each target's job status, so the hook judges the precondition there; the
    // persist then updates the targets that still meet it, stamped, and a retry enqueues each
    // target's new sending job in the same transaction. A delete is refused, in its one validation
    // round, while a row may still be sent.
    /// <summary>
    ///     The outgoing emails' stack service: the reads, the delete, the <c>retry</c> and
    ///     <c>cancel</c> actions, and the enlisted inserts the outbox makes, each of which enqueues
    ///     the row's sending job in the same persist.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each job runs as the system user and tells the administrators, never a requester, when
    ///         it fails; it becomes due at the row's <see cref="OutgoingEmail.SendAfter" />.
    ///     </para>
    ///     <para>
    ///         A row is retried when it failed and its job ended, or when it was abandoned: still
    ///         unsent with no job that may run it. The retry makes it pending again with the send's
    ///         result and the delivery columns cleared, keeps its attempt count and its correlation,
    ///         and gives it a new sending job. A row is cancelled only while it is pending, even on a
    ///         read-only tenant, where the outbox keeps sending. A row may be deleted once it is
    ///         settled or abandoned.
    ///     </para>
    /// </remarks>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="jobs">The scope's job queue, which the persist enqueues the sending jobs through.</param>
    public sealed class OutgoingEmailService(IEntityPipelineFactory factory, IJobQueue jobs) : EntityService<OutgoingEmail>(factory)
    {
        /// <summary>The key of the handler that sends an outgoing email.</summary>
        public const string SendHandlerKey = "core.outgoing-email";

        /// <summary>The action that sends a failed or abandoned email again.</summary>
        public const string RetryAction = "retry";

        /// <summary>The action that stops a pending email from being sent.</summary>
        public const string CancelAction = "cancel";

        /// <summary>The states a retried row may be in: failed, or unsent and abandoned.</summary>
        private static readonly FilterTree RetryCondition = FilterTree.Leaf(
            $"{nameof(OutgoingEmail.State)} = '{nameof(OutgoingEmailState.Failed)}' or {nameof(OutgoingEmail.State)} = '{nameof(OutgoingEmailState.Pending)}' or {nameof(OutgoingEmail.State)} = '{nameof(OutgoingEmailState.Sending)}'");

        /// <summary>The state a cancelled row must still be in.</summary>
        private static readonly FilterTree CancelCondition = FilterTree.Leaf($"{nameof(OutgoingEmail.State)} = '{nameof(OutgoingEmailState.Pending)}'");

        /// <summary>A retry's effect: pending again, with the send's result and the delivery columns cleared; the attempt count stays.</summary>
        private static readonly Dictionary<string, object?> RetryAssignments = new(StringComparer.Ordinal)
        {
            [nameof(OutgoingEmail.State)] = OutgoingEmailState.Pending,
            [nameof(OutgoingEmail.SentAt)] = null,
            [nameof(OutgoingEmail.ProviderMessageId)] = null,
            [nameof(OutgoingEmail.ExpectsDeliveryEvents)] = false,
            [nameof(OutgoingEmail.ErrorCode)] = null,
            [nameof(OutgoingEmail.ErrorDetail)] = null,
            [nameof(OutgoingEmail.DeliveryStatus)] = null,
            [nameof(OutgoingEmail.DeliveryAt)] = null,
            [nameof(OutgoingEmail.DeliveryRecipient)] = null,
            [nameof(OutgoingEmail.DeliveryReason)] = null,
            [nameof(OutgoingEmail.LastProviderEventId)] = null,
        };

        /// <summary>A cancel's effect.</summary>
        private static readonly Dictionary<string, object?> CancelAssignments = new(StringComparer.Ordinal)
        {
            [nameof(OutgoingEmail.State)] = OutgoingEmailState.Cancelled,
        };

        /// <summary>Whether a row is settled: its job, when it runs, sends nothing.</summary>
        /// <param name="state">The row's state.</param>
        /// <returns>True for a settled row.</returns>
        public static bool IsSettled(OutgoingEmailState state)
        {
            return state is not (OutgoingEmailState.Pending or OutgoingEmailState.Sending);
        }

        /// <summary>Whether a job is live: it may still run.</summary>
        /// <param name="status">The job's status.</param>
        /// <returns>True for a live job.</returns>
        public static bool IsLive(JobStatus status)
        {
            return status is JobStatus.Pending or JobStatus.Running or JobStatus.Held or JobStatus.PendingConfirmation;
        }

        /// <summary>Whether a row is orphaned: unsettled, with no job or a job that is no longer live.</summary>
        /// <param name="state">The row's state.</param>
        /// <param name="job">The status of the row's job; null when it has none.</param>
        /// <returns>True for an orphaned row.</returns>
        public static bool IsOrphaned(OutgoingEmailState state, JobStatus? job)
        {
            return !IsSettled(state) && (job is not JobStatus status || !IsLive(status));
        }

        /// <summary>Whether a row may be sent again: orphaned, or failed with no live job.</summary>
        /// <param name="state">The row's state.</param>
        /// <param name="job">The status of the row's job; null when it has none.</param>
        /// <returns>True for a row a retry accepts.</returns>
        public static bool IsRetryable(OutgoingEmailState state, JobStatus? job)
        {
            return IsOrphaned(state, job) || (state == OutgoingEmailState.Failed && (job is not JobStatus status || !IsLive(status)));
        }

        /// <summary>Whether a row may be deleted: settled, or orphaned.</summary>
        /// <param name="state">The row's state.</param>
        /// <param name="job">The status of the row's job; null when it has none.</param>
        /// <returns>True for a row a delete accepts.</returns>
        public static bool IsDeletable(OutgoingEmailState state, JobStatus? job)
        {
            return IsSettled(state) || IsOrphaned(state, job);
        }

        /// <summary>
        ///     Makes each target pending again with the send's result and the delivery columns cleared,
        ///     and gives it a new sending job, due at its send-after instant.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the update and the enqueue are composed.</returns>
        [EntityAction(RetryAction, Action = AccessActions.Retry, Description = "Sends each failed or abandoned email again.")]
        public async Task RetryAsync(ActionContext<OutgoingEmail, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            // --- The row's effect, on every target, each stamped: a target no longer failed or unsent
            // (another administrator cancelled it since the first round) refuses the whole retry
            // rather than being skipped, so no job is enqueued for a row the update passed over; the
            // job queue checks under its update's lock that no live job holds the row, and refuses the
            // whole persist if one does ---
            context.Update(RetryAssignments, RetryCondition, OutgoingEmailCodes.NotRetryable);
            JobRequest[] requests = [.. context.Entities.Select(static row => SendRequest(row))];
            if (requests.Length > 0)
            {
                _ = jobs.Enqueue(context.Batch, requests);
            }
        }

        /// <summary>Cancels each target before it is sent; its job, when claimed, meets a settled row and succeeds.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the update is composed.</returns>
        [EntityAction(CancelAction, Action = AccessActions.Cancel, Destructive = true, AllowWhenReadOnly = true, Description = "Stops each pending email from being sent.")]
        [SuppressMessage(
            "Performance",
            "CA1822:Mark members as static",
            Justification = "An entity action is an instance method of its stack's service: the pipeline reaches it only through the service.")]
        public async Task CancelAsync(ActionContext<OutgoingEmail, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            // A target the sending job pre-marked since the first round may be on the wire, so the
            // update repeats the precondition and leaves it as it is.
            context.Update(CancelAssignments, CancelCondition);
        }

        /// <summary>Enqueues the sending job of every new row that has none, in the persist that inserts it.</summary>
        /// <param name="context">The persist context.</param>
        /// <returns>A task that completes when the enqueue is appended.</returns>
        protected override async Task ContributeAsync(PersistContext<OutgoingEmail> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ContributeAsync(context).ConfigureAwait(false);
            if (context is not SavePersistContext<OutgoingEmail> save)
            {
                return;
            }

            // --- One job per new row, due at its send-after instant, run by the platform ---
            // The job queue checks the stored JobId under its update's lock and sets it in the same
            // transaction; a row a later writer re-saves keeps the job it has.
            List<JobRequest> requests = [];
            for (int index = 0; index < save.Entities.Count; index++)
            {
                OutgoingEmail row = save.Entities[index];
                if (save.Before(index) is null && row.JobId is null)
                {
                    requests.Add(SendRequest(row));
                }
            }

            if (requests.Count > 0)
            {
                _ = jobs.Enqueue(save.Batch, requests);
            }
        }

        // Spec 0022 §7.1's preconditions, judged on the targets and their jobs' statuses, both read
        // in the first round: a refused target fails the whole action, at its position among the ids.
        /// <summary>Judges each target's state, and its job's, against the action's precondition.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when every target is judged.</returns>
        protected override async Task ValidateActionAsync(string action, ActionContext<OutgoingEmail, int> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateActionAsync(action, context).ConfigureAwait(false);

            // --- The targets and their jobs, in the first round ---
            ContextRef<RawResult>? statuses = action == RetryAction ? JobStatuses(context.Loader, context.Ids) : null;
            await context.Loader.LoadAsync().ConfigureAwait(false);
            Dictionary<int, JobStatus> jobsOf = statuses is null ? [] : Read(statuses.Value);

            // --- Each target's state ---
            for (int index = 0; index < context.Entities.Count; index++)
            {
                OutgoingEmail row = context.Entities[index];
                JobStatus? job = jobsOf.TryGetValue(row.Id, out JobStatus status) ? status : null;
                string? refusal = action switch
                {
                    RetryAction when !IsRetryable(row.State, job) => OutgoingEmailCodes.NotRetryable,
                    CancelAction when row.State != OutgoingEmailState.Pending => OutgoingEmailCodes.NotCancellable,
                    _ => null,
                };

                if (refusal is not null)
                {
                    context.Errors.Add(AccessRefusals.Target(context.Ids, row.Id, index), refusal);
                }
            }
        }

        /// <summary>
        ///     Refuses the delete of every target that is unsettled with a live job, which may still be
        ///     sent: <see cref="OutgoingEmailCodes.NotDeletable" /> at its id.
        /// </summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the targets are judged.</returns>
        protected override async Task ValidateDeleteAsync(DeleteContext<OutgoingEmail, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateDeleteAsync(context).ConfigureAwait(false);

            // --- The targets and their jobs' statuses load with the first round ---
            ContextRef<RawResult> statuses = JobStatuses(context.Loader, context.Ids);
            await context.Loader.LoadAsync().ConfigureAwait(false);
            Dictionary<int, JobStatus> jobsOf = Read(statuses.Value);

            for (int position = 0; position < context.Entities.Count; position++)
            {
                OutgoingEmail target = context.Entities[position];
                JobStatus? job = jobsOf.TryGetValue(target.Id, out JobStatus status) ? status : null;
                if (!IsDeletable(target.State, job))
                {
                    context.Errors.Add(AccessRefusals.Target(context.Ids, target.Id, position), OutgoingEmailCodes.NotDeletable);
                }
            }
        }

        /// <summary>The request of a row's sending job: run by the platform, requested by no one, due at the row's send-after instant.</summary>
        /// <param name="row">The row, which the job owns.</param>
        /// <returns>The request.</returns>
        private static JobRequest SendRequest(OutgoingEmail row)
        {
            return new JobRequest(
                SendHandlerKey,
                Arguments: null,
                DueAt: row.SendAfter,
                RequestedById: null,
                RunAsUserId: WellKnownIds.SystemUserId,
                Entity: row);
        }

        // A load without a grant filter, beside the targets in the first round: the status of each
        // named row's job, through the row's stored JobId. A row with no job reads no row here; the
        // caller consults it only for the targets the first round found visible.
        /// <summary>Requests the job status of each named row that has a job.</summary>
        /// <param name="loader">The context's loader.</param>
        /// <param name="ids">The ids the request named.</param>
        /// <returns>The pending load: one result set of row ids and job statuses.</returns>
        private static ContextRef<RawResult> JobStatuses(IContextLoader loader, IReadOnlyList<int> ids)
        {
            List<IdList> keys = [.. ids.Distinct().Order().Select(static id => new IdList { Id = id })];
            return loader.Sql(
                $"SELECT [e].[Id], [j].[Status] FROM [core].[OutgoingEmails] AS [e] INNER JOIN {keys} AS [k] ON [k].[Id] = [e].[Id] INNER JOIN [core].[Jobs] AS [j] ON [j].[Id] = [e].[JobId];",
                new SqlOptions(resultSets: 1));
        }

        /// <summary>Reads the job statuses the first round loaded.</summary>
        /// <param name="result">The load's result.</param>
        /// <returns>Each row's job status, by the row's id.</returns>
        private static Dictionary<int, JobStatus> Read(RawResult result)
        {
            QueryRowSet set = result.ResultSets[0];
            int[] rows = (int[])set.GetBuffer(0);
            string?[] statuses = (string?[])set.GetBuffer(1);
            Dictionary<int, JobStatus> read = new(set.RowCount);
            for (int row = 0; row < set.RowCount; row++)
            {
                read[rows[row]] = Enum.Parse<JobStatus>(statuses[row]!);
            }

            return read;
        }
    }
}
