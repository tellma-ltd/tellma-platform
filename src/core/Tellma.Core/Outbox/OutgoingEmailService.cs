// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §3.2 step 5 and §7.1: the outgoing emails' stack service. Rows arrive only as the
    // outbox's enlisted inserts, and each new row's sending job is enqueued in the same persist; a
    // delete is refused while a row may still be sent.
    /// <summary>
    ///     The outgoing emails' stack service: the reads, the delete, and the enlisted inserts the
    ///     outbox makes, each of which enqueues the row's sending job in the same persist.
    /// </summary>
    /// <remarks>
    ///     Each job runs as the system user and tells the administrators, never a requester, when it
    ///     fails; it becomes due at the row's <see cref="OutgoingEmail.SendAfter" />. A row may be
    ///     deleted once it is settled, or once it is orphaned: no job, or a job that is no longer live.
    /// </remarks>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="jobs">The scope's job queue, which the persist enqueues the sending jobs through.</param>
    public sealed class OutgoingEmailService(IEntityPipelineFactory factory, IJobQueue jobs) : EntityService<OutgoingEmail>(factory)
    {
        /// <summary>The key of the handler that sends an outgoing email.</summary>
        public const string SendHandlerKey = "core.outgoing-email";

        /// <summary>The validation path segment of a delete's target ids.</summary>
        private const string IdsProperty = "Ids";

        /// <summary>The job projection a delete's precondition reads.</summary>
        private const string JobSelect = "Id,Status";

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
                    requests.Add(new JobRequest(
                        SendHandlerKey,
                        Arguments: null,
                        DueAt: row.SendAfter,
                        RequestedById: null,
                        RunAsUserId: WellKnownIds.SystemUserId,
                        Entity: row));
                }
            }

            if (requests.Count > 0)
            {
                _ = jobs.Enqueue(save.Batch, requests);
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

            // --- The targets load with the first round, every mapped property included ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            List<(OutgoingEmail Target, int Position, int JobId)> unsettled = [];
            for (int position = 0; position < context.Entities.Count; position++)
            {
                OutgoingEmail target = context.Entities[position];
                if (!IsSettled(target.State) && target.JobId is int jobId)
                {
                    unsettled.Add((target, position, jobId));
                }
            }

            // A settled row, or one whose job retention deleted, is deletable without a second round.
            if (unsettled.Count == 0)
            {
                return;
            }

            // --- The unsettled targets' jobs, in a second round: a live one keeps its row ---
            ContextRef<IReadOnlyDictionary<int, Job>> loaded = context.Loader.ByIds<Job, int>([.. unsettled.Select(static item => item.JobId).Distinct()], JobSelect);
            await context.Loader.LoadAsync().ConfigureAwait(false);
            foreach ((OutgoingEmail target, int position, int jobId) in unsettled)
            {
                if (loaded.Value.TryGetValue(jobId, out Job? job) && IsLive(job.Status))
                {
                    context.Errors.Add(TargetPath(context.Ids, target.Id, position), OutgoingEmailCodes.NotDeletable);
                }
            }
        }

        /// <summary>The path of a delete's target: <c>Ids[i]</c>, at the first position the request names the id.</summary>
        /// <param name="ids">The ids as the request named them.</param>
        /// <param name="id">The target's id.</param>
        /// <param name="position">The target's position among the loaded targets, the fallback.</param>
        /// <returns>The path.</returns>
        private static ValidationPath TargetPath(IReadOnlyList<int> ids, int id, int position)
        {
            int index = -1;
            for (int candidate = 0; candidate < ids.Count; candidate++)
            {
                if (ids[candidate] == id)
                {
                    index = candidate;
                    break;
                }
            }

            return ValidationPath.Root.Property(IdsProperty).Index(index < 0 ? position : index);
        }
    }
}
