// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §10: a completion that ends a job Failed raises core.job.failed, and one that ends it
    // PendingConfirmation raises core.job.pending-confirmation, each to the requester, else to the
    // tenant's administrators; job.changed goes to the requester alone, never to administrators. The
    // gap hold of §6.10 raises core.scheduler.gap to the administrators and core.job.held to each
    // requester of a held row, neither with a target.
    /// <summary>The notifications and client events a completion raises.</summary>
    internal static class JobNotices
    {
        /// <summary>The category of the job notifications.</summary>
        public const string Category = "jobs";

        /// <summary>The notification of a job that failed.</summary>
        public const string FailedType = "core.job.failed";

        /// <summary>The notification of a job waiting for its initiator to confirm warnings.</summary>
        public const string PendingConfirmationType = "core.job.pending-confirmation";

        /// <summary>The notification of a requester's jobs held after a gap in the worker's polls.</summary>
        public const string HeldType = "core.job.held";

        /// <summary>The notification of a gap in the worker's polls.</summary>
        public const string GapType = "core.scheduler.gap";

        /// <summary>The client event of a job whose row changed.</summary>
        public const string ChangedEvent = "job.changed";

        /// <summary>The descriptor of <see cref="FailedType" />.</summary>
        public static NotificationTypeDescriptor FailedDescriptor { get; } = new(
            FailedType, Category, Mutable: false, TargetResource: CoreResources.Job, Severity: NotificationSeverity.Error, Toast: true);

        /// <summary>The descriptor of <see cref="PendingConfirmationType" />.</summary>
        public static NotificationTypeDescriptor PendingConfirmationDescriptor { get; } = new(
            PendingConfirmationType, Category, Mutable: false, TargetResource: CoreResources.Job, Severity: NotificationSeverity.Warning, Toast: true);

        /// <summary>The descriptor of <see cref="HeldType" />.</summary>
        public static NotificationTypeDescriptor HeldDescriptor { get; } = new(
            HeldType, Category, Mutable: false, TargetResource: null, Severity: NotificationSeverity.Warning, Toast: true);

        /// <summary>The descriptor of <see cref="GapType" />.</summary>
        public static NotificationTypeDescriptor GapDescriptor { get; } = new(
            GapType, Category, Mutable: false, TargetResource: null, Severity: NotificationSeverity.Warning, Toast: true);

        /// <summary>The dedup key of a job's confirmation notice, which its withdrawal names.</summary>
        /// <param name="jobId">The job.</param>
        /// <returns>The key.</returns>
        public static string PendingConfirmationKey(int jobId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{PendingConfirmationType}:{jobId}");
        }

        // Spec 0020 §6.10: one core.scheduler.gap to the administrators, split so no request names more
        // recipients than the notifier takes, with the sum of every held row's count; one core.job.held
        // to each requester with their own count. Both dedup on their type, so a repeat while the first
        // is unread adds nothing.
        /// <summary>The notices a gap hold raises.</summary>
        /// <param name="gap">What the gap check found.</param>
        /// <param name="administrators">The tenant's administrators.</param>
        /// <param name="maxRecipientsPerRequest">The most recipients one request may name.</param>
        /// <returns>The requests: the gap notice's first, then one per requester.</returns>
        public static IEnumerable<NotificationRequest> Gap(JobGapCheck gap, IReadOnlyList<int> administrators, int maxRecipientsPerRequest)
        {
            ArgumentNullException.ThrowIfNull(gap);
            ArgumentNullException.ThrowIfNull(administrators);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxRecipientsPerRequest, 1);
            if (gap.PreviousTickAt is not DateTimeOffset previousTickAt)
            {
                yield break;
            }

            SchedulerGapArguments arguments = new(previousTickAt, gap.Now, gap.HeldCount);
            foreach (int[] recipients in administrators.Distinct().Chunk(maxRecipientsPerRequest))
            {
                yield return new NotificationRequest(GapType, recipients, arguments, TargetResource: null, TargetId: null, ActorUserId: null, DedupKey: GapType);
            }

            foreach (JobHeldCount held in gap.Held)
            {
                if (held.RequestedById is int requester && held.Count > 0)
                {
                    yield return new NotificationRequest(
                        HeldType, [requester], new JobHeldArguments(held.Count), TargetResource: null, TargetId: null, ActorUserId: null, DedupKey: HeldType);
                }
            }
        }

        /// <summary>The event telling the requester a job changed.</summary>
        /// <param name="jobId">The job.</param>
        /// <param name="requesterId">The job's requester.</param>
        /// <returns>The event.</returns>
        public static ClientEvent Changed(int jobId, int requesterId)
        {
            return new ClientEvent(ChangedEvent, [requesterId], new JobChangedPayload(jobId));
        }

        /// <summary>Whether an outcome raises a notice: a failure or a wait for confirmation.</summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>True when it raises one.</returns>
        public static bool Raises(JobOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            return outcome.Kind is JobOutcomeKind.Failed or JobOutcomeKind.PendingConfirmation;
        }

        /// <summary>The notice an outcome raises; null when it raises none, or when it has no one to go to.</summary>
        /// <param name="job">The job.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="options">The handler's options, which say whether a failure notice stays in the inbox.</param>
        /// <param name="administrators">The tenant's administrators, who receive the notice of a job without a requester.</param>
        /// <returns>The request, or null.</returns>
        public static NotificationRequest? Of(Job job, JobOutcome outcome, JobHandlerAttribute options, IReadOnlyList<int> administrators)
        {
            ArgumentNullException.ThrowIfNull(job);
            ArgumentNullException.ThrowIfNull(outcome);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(administrators);
            IReadOnlyList<int> recipients = job.RequestedById is int requester ? [requester] : administrators;
            return recipients.Count == 0 ? null : outcome.Kind switch
            {
                JobOutcomeKind.Failed => new NotificationRequest(
                    FailedType,
                    recipients,
                    new JobFailedArguments(job.HandlerKey, outcome.ErrorCode!),
                    CoreResources.Job,
                    job.Id,
                    ActorUserId: null,
                    DedupKey: null,
                    InboxOnly: options.InboxOnlyFailureNotices),
                JobOutcomeKind.PendingConfirmation => new NotificationRequest(
                    PendingConfirmationType,
                    recipients,
                    new JobPendingConfirmationArguments(job.HandlerKey, CountOf(outcome.PendingConfirmationsJson!)),
                    CoreResources.Job,
                    job.Id,
                    ActorUserId: null,
                    DedupKey: PendingConfirmationKey(job.Id)),
                JobOutcomeKind.Succeeded or JobOutcomeKind.Retry or JobOutcomeKind.Released or JobOutcomeKind.Cancelled => null,
                _ => throw new InvalidOperationException($"The outcome kind {outcome.Kind} is not defined."),
            };
        }

        /// <summary>The warnings raised, as the pending record counts them.</summary>
        /// <param name="pendingConfirmationsJson">The record.</param>
        /// <returns>The count.</returns>
        private static int CountOf(string pendingConfirmationsJson)
        {
            using var record = JsonDocument.Parse(pendingConfirmationsJson);
            return record.RootElement.GetProperty("count").GetInt32();
        }
    }

    /// <summary>The payload of <c>job.changed</c>.</summary>
    /// <param name="JobId">The job.</param>
    internal sealed record JobChangedPayload(int JobId);

    /// <summary>The arguments of <c>core.job.failed</c>.</summary>
    /// <param name="HandlerKey">The job's handler key.</param>
    /// <param name="ErrorCode">The code the failure recorded.</param>
    internal sealed record JobFailedArguments(string HandlerKey, string ErrorCode);

    /// <summary>The arguments of <c>core.job.pending-confirmation</c>.</summary>
    /// <param name="HandlerKey">The job's handler key.</param>
    /// <param name="Count">The warnings raised.</param>
    internal sealed record JobPendingConfirmationArguments(string HandlerKey, int Count);

    /// <summary>The arguments of <c>core.job.held</c>.</summary>
    /// <param name="Count">The requester's held jobs.</param>
    internal sealed record JobHeldArguments(int Count);

    /// <summary>The arguments of <c>core.scheduler.gap</c>.</summary>
    /// <param name="PreviousTickAt">The last poll before the gap.</param>
    /// <param name="Now">The poll that found the gap.</param>
    /// <param name="HeldCount">Every held job, whoever requested it.</param>
    internal sealed record SchedulerGapArguments(DateTimeOffset PreviousTickAt, DateTimeOffset Now, int HeldCount);
}
