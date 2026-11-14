// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     One claimed job as a handler sees it: the job row, the item it processes, its cancellation,
    ///     its progress, and the outcome the handler marks it with.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An item the handler marks keeps its outcome, and the last of <see cref="Succeed" />,
    ///         <see cref="Retry" /> and <see cref="Fail" /> wins. An item left unmarked follows how the
    ///         handler ended: a normal return succeeds it, <see cref="JobFailedException" /> fails it, a
    ///         validation failure fails it with its first error, an unconfirmed warning leaves it
    ///         waiting for confirmation, <see cref="OperationCanceledException" /> releases it (when the
    ///         handler never touched it) or retries it at once, and any other exception retries it
    ///         with an internal error. An unmarked item whose <see cref="CancellationReason" /> is
    ///         <see cref="JobCancellationReason.CancelRequested" /> ends cancelled however the handler
    ///         ended, and when any item of the batch lost its lease, no outcome is kept at all. A
    ///         handler never marks an item cancelled itself.
    ///     </para>
    ///     <para>
    ///         Items are safe to mark from the handler's own concurrent work, one item per flow; the
    ///         worker reads the marks once the handler has returned. A class rather than a record:
    ///         an item has identity, and a copy would carry marks the worker never reads.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TItem">The item's type: <see cref="Jobs.Job" /> for an arguments-only handler, the entity for an entity-backed one.</typeparam>
    public sealed class JobItem<TItem>
        where TItem : class
    {
        // The reason as its underlying value, so the worker's renewal and the handler can race on it
        // through Interlocked.
        private int _cancellationReason;
        private JobItemMark? _mark;
        private readonly List<NotificationRequest> _successNotifications = [];

        /// <summary>The job row as the claim returned it.</summary>
        public required Job Job
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Job));
        }

        /// <summary>
        ///     The item to process: the job itself for an arguments-only handler, the claimed entity row
        ///     for an entity-backed one. Never null: a job whose entity is missing never reaches its
        ///     handler.
        /// </summary>
        public required TItem Item
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Item));
        }

        /// <summary>
        ///     Cancelled when the item must stop, with <see cref="CancellationReason" /> saying why: alone
        ///     when someone asks to stop this job, or with its whole batch when the lease is lost or the
        ///     host is stopping. For a batch of one, the item's and the batch's tokens coincide.
        /// </summary>
        public CancellationToken CancellationToken { get; init; }

        /// <summary>
        ///     Why <see cref="CancellationToken" /> was cancelled; <see cref="JobCancellationReason.None" />
        ///     until it is. Once set it changes only to <see cref="JobCancellationReason.LeaseLost" />.
        /// </summary>
        public JobCancellationReason CancellationReason => (JobCancellationReason)Volatile.Read(ref _cancellationReason);

        /// <summary>Reports the item's progress and checkpoints its state.</summary>
        public required IJobProgress Progress
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Progress));
        }

        /// <summary>
        ///     The current checkpoint as JSON: the one the claim returned, or the one the handler last
        ///     wrote through a renewal or a committed append. The worker binds it into the completion.
        /// </summary>
        internal string? StateJson
        {
            get => Volatile.Read(ref field);
            set => Volatile.Write(ref field, value);
        }

        /// <summary>The outcome the handler marked the item with; null when it marked none.</summary>
        internal JobItemMark? Mark => Volatile.Read(ref _mark);

        /// <summary>The notifications to raise if the item's final outcome is a success, in the order queued.</summary>
        internal IReadOnlyList<NotificationRequest> SuccessNotifications
        {
            get
            {
                lock (_successNotifications)
                {
                    return [.. _successNotifications];
                }
            }
        }

        /// <summary>
        ///     Reads the job's arguments, under the platform's JSON rules and tolerantly: an unknown
        ///     member is skipped and a missing one, a <c>required</c> one included, keeps its default.
        ///     Each call reads afresh.
        /// </summary>
        /// <typeparam name="TArgs">The arguments' type.</typeparam>
        /// <returns>
        ///     The arguments. A job enqueued without arguments reads as <see langword="null" /> for a
        ///     nullable value type, the default for another value type, and a fresh empty instance for an
        ///     object, dictionary or collection type. Nullable annotations on a reference type cannot be
        ///     told apart at run time, so a handler that must know whether arguments were given reads
        ///     <see cref="Job.ArgumentsJson" />.
        /// </returns>
        /// <exception cref="System.Text.Json.JsonException">The stored arguments do not fit <typeparamref name="TArgs" />.</exception>
        /// <exception cref="NotSupportedException"><typeparamref name="TArgs" /> cannot be created from JSON.</exception>
        public TArgs Arguments<TArgs>()
        {
            return JobJson.ReadArguments<TArgs>(Job.ArgumentsJson);
        }

        /// <summary>
        ///     Reads the item's current checkpoint, under the same tolerant rules as
        ///     <see cref="Arguments{TArgs}" />: the one the claim returned, or the one the handler last
        ///     wrote through <see cref="IJobProgress.Report" /> once a renewal or a flush carried it, or
        ///     through <see cref="IJobProgress.Append" /> once its batch committed. Each call reads afresh.
        /// </summary>
        /// <typeparam name="TState">The checkpoint's type.</typeparam>
        /// <returns>The checkpoint; the type's default when the job has none.</returns>
        /// <exception cref="System.Text.Json.JsonException">The stored checkpoint does not fit <typeparamref name="TState" />.</exception>
        public TState? State<TState>()
        {
            return JobJson.ReadState<TState>(StateJson);
        }

        /// <summary>Marks the item succeeded.</summary>
        public void Succeed()
        {
            Volatile.Write(ref _mark, JobItemMark.Succeeded);
        }

        /// <summary>
        ///     Marks the item for another attempt. Once the job has used its last allowed attempt, a retry
        ///     fails it instead, with <paramref name="error" /> or as exhausted.
        /// </summary>
        /// <param name="after">
        ///     How long to wait before the next attempt, used as given up to
        ///     <see cref="JobLimits.MaxRetryDelay" />, to which a longer delay is shortened; null for the
        ///     handler's backoff.
        /// </param>
        /// <param name="error">The error to record on the job meanwhile; null for none.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="after" /> is negative.</exception>
        /// <exception cref="ArgumentException">
        ///     The error's code is longer than <see cref="JobLimits.MaxKeyLength" /> characters, or its
        ///     arguments take more than <see cref="JobLimits.MaxArgumentsLength" /> once serialised.
        /// </exception>
        public void Retry(TimeSpan? after = null, JobError? error = null)
        {
            // --- The delay: never negative, shortened to the ceiling ---
            if (after is TimeSpan delay)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(after));
                after = delay > JobLimits.MaxRetryDelay ? JobLimits.MaxRetryDelay : delay;
            }

            // --- The error must fit the job table; checked before anything is marked ---
            string? arguments = error is null ? null : JobJson.SerializeArguments(error, nameof(error));
            Volatile.Write(ref _mark, new JobItemMark(JobItemMarkKind.Retry, after, error, arguments));
        }

        /// <summary>Marks the item failed with an error, for good: a failed job is never retried on its own.</summary>
        /// <param name="error">The error to record on the job.</param>
        /// <exception cref="ArgumentNullException"><paramref name="error" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The error's code is longer than <see cref="JobLimits.MaxKeyLength" /> characters, or its
        ///     arguments take more than <see cref="JobLimits.MaxArgumentsLength" /> once serialised.
        /// </exception>
        public void Fail(JobError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            string? arguments = JobJson.SerializeArguments(error, nameof(error));
            Volatile.Write(ref _mark, new JobItemMark(JobItemMarkKind.Failed, null, error, arguments));
        }

        /// <summary>
        ///     Queues a notification that the worker raises in the completion's transaction only if the
        ///     item's final outcome is a success. Each call queues one more.
        /// </summary>
        /// <param name="request">The notification.</param>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        public void NotifyOnSuccess(NotificationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            lock (_successNotifications)
            {
                _successNotifications.Add(request);
            }
        }

        /// <summary>
        ///     Records why the item's token is being cancelled, before the worker cancels it. The first
        ///     reason sticks, except that <see cref="JobCancellationReason.LeaseLost" /> replaces any
        ///     other and is replaced by none.
        /// </summary>
        /// <param name="reason">The reason; never <see cref="JobCancellationReason.None" />.</param>
        /// <returns>True when the reason changed.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason" /> is <see cref="JobCancellationReason.None" /> or undefined.</exception>
        internal bool TrySetCancellationReason(JobCancellationReason reason)
        {
            if (reason == JobCancellationReason.None || !Enum.IsDefined(reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "A cancellation reason names why the token is cancelled.");
            }

            // Compare-and-swap, since a renewal (a requested cancel or a lost lease) and the host's
            // stop can race for the same item.
            while (true)
            {
                int current = Volatile.Read(ref _cancellationReason);
                bool replaces = (JobCancellationReason)current switch
                {
                    JobCancellationReason.None => true,
                    JobCancellationReason.LeaseLost => false,
                    JobCancellationReason.CancelRequested or JobCancellationReason.HostStopping => reason == JobCancellationReason.LeaseLost,
                    _ => throw new InvalidOperationException("The item holds an undefined cancellation reason."),
                };

                if (!replaces)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _cancellationReason, (int)reason, current) == current)
                {
                    return true;
                }
            }
        }
    }
}
