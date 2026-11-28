// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Testing.Jobs
{
    /// <summary>
    ///     Lets a job handler's unit test play the worker's part: build the items a claim would hand
    ///     the handler, seed a checkpoint to test a resume, set the reason an item is being cancelled,
    ///     and read back what the handler marked and the notifications it queued for success.
    /// </summary>
    /// <remarks>
    ///     The probe reads and writes exactly what the worker does, so a handler tested through it is
    ///     held to the same contract it meets in production. It never runs the worker's own rules: an
    ///     unmarked item stays unmarked here, whereas the worker settles it from how the handler ended.
    /// </remarks>
    public static class JobItemProbe
    {
        /// <summary>A progress reporter that records nothing, for items built without one.</summary>
        private static readonly IJobProgress NoProgress = new NullJobProgress();

        /// <summary>Builds an item of an arguments-only handler, whose item is the job itself.</summary>
        /// <param name="job">The job, as a claim would return it.</param>
        /// <param name="checkpoint">The checkpoint the job resumes from; null for none.</param>
        /// <param name="progress">The item's progress reporter; null for one that records nothing.</param>
        /// <param name="cancellationToken">The item's token; none by default.</param>
        /// <returns>The item.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="job" /> is null.</exception>
        public static JobItem<Job> ForJob(Job job, object? checkpoint = null, IJobProgress? progress = null, CancellationToken cancellationToken = default)
        {
            return ForItem(job, job, checkpoint, progress, cancellationToken);
        }

        /// <summary>Builds an item of an entity-backed handler: the job and the row it processes.</summary>
        /// <typeparam name="TItem">The row's type.</typeparam>
        /// <param name="job">The job, as a claim would return it.</param>
        /// <param name="item">The row the job processes.</param>
        /// <param name="checkpoint">The checkpoint the job resumes from; null for none.</param>
        /// <param name="progress">The item's progress reporter; null for one that records nothing.</param>
        /// <param name="cancellationToken">The item's token; none by default.</param>
        /// <returns>The item.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="job" /> or <paramref name="item" /> is null.</exception>
        public static JobItem<TItem> ForItem<TItem>(Job job, TItem item, object? checkpoint = null, IJobProgress? progress = null, CancellationToken cancellationToken = default)
            where TItem : class
        {
            JobItem<TItem> result = new()
            {
                Job = job,
                Item = item,
                Progress = progress ?? NoProgress,
                CancellationToken = cancellationToken,
            };
            SetCheckpoint(result, checkpoint);
            return result;
        }

        /// <summary>
        ///     Replaces an item's current checkpoint, serialised under the platform's JSON rules as the
        ///     worker stores it, so <see cref="JobItem{TItem}.State{TState}" /> reads it back.
        /// </summary>
        /// <typeparam name="TItem">The item's type.</typeparam>
        /// <param name="item">The item.</param>
        /// <param name="checkpoint">The checkpoint; null for none.</param>
        /// <exception cref="ArgumentNullException"><paramref name="item" /> is null.</exception>
        public static void SetCheckpoint<TItem>(JobItem<TItem> item, object? checkpoint)
            where TItem : class
        {
            ArgumentNullException.ThrowIfNull(item);
            item.StateJson = checkpoint is null ? null : JsonSerializer.Serialize(checkpoint, checkpoint.GetType(), TellmaJson.Options);
        }

        /// <summary>
        ///     Records why an item is being cancelled, as the worker does before it cancels the item's
        ///     token; the test cancels the token itself. The first reason sticks, except that
        ///     <see cref="JobCancellationReason.LeaseLost" /> replaces any other and is replaced by none.
        /// </summary>
        /// <typeparam name="TItem">The item's type.</typeparam>
        /// <param name="item">The item.</param>
        /// <param name="reason">The reason; never <see cref="JobCancellationReason.None" />.</param>
        /// <returns>True when the reason changed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason" /> is <see cref="JobCancellationReason.None" /> or undefined.</exception>
        public static bool SetCancellationReason<TItem>(JobItem<TItem> item, JobCancellationReason reason)
            where TItem : class
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.TrySetCancellationReason(reason);
        }

        /// <summary>The outcome the handler marked an item with.</summary>
        /// <typeparam name="TItem">The item's type.</typeparam>
        /// <param name="item">The item.</param>
        /// <returns>The marked outcome; null when the handler marked none.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item" /> is null.</exception>
        public static JobItemOutcome? OutcomeOf<TItem>(JobItem<TItem> item)
            where TItem : class
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Mark is not { } mark)
            {
                return null;
            }

            // The internal mark kinds map one to one onto the public outcome kinds.
            JobItemOutcomeKind kind = mark.Kind switch
            {
                JobItemMarkKind.Succeeded => JobItemOutcomeKind.Succeeded,
                JobItemMarkKind.Retry => JobItemOutcomeKind.Retry,
                JobItemMarkKind.Failed => JobItemOutcomeKind.Failed,
                _ => throw new InvalidOperationException("The item holds an undefined mark."),
            };
            return new JobItemOutcome(kind, mark.RetryAfter, mark.Error);
        }

        /// <summary>The notifications the handler queued on an item for its success, in the order queued.</summary>
        /// <typeparam name="TItem">The item's type.</typeparam>
        /// <param name="item">The item.</param>
        /// <returns>A copy of the queued notifications.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item" /> is null.</exception>
        public static IReadOnlyList<NotificationRequest> SuccessNotificationsOf<TItem>(JobItem<TItem> item)
            where TItem : class
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.SuccessNotifications;
        }

        /// <summary>A progress reporter that records nothing.</summary>
        private sealed class NullJobProgress : IJobProgress
        {
            /// <inheritdoc />
            public void Report(byte? percent, JobMessage? message, object? state)
            {
            }

            /// <inheritdoc />
            public Task FlushAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state)
            {
            }
        }
    }
}
