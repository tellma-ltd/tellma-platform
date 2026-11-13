// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     Declares, on a class implementing <see cref="IJobHandler" /> or
    ///     <see cref="IEntityJobHandler{TEntity}" />, the handler key it runs and how the worker claims,
    ///     leases and retries its jobs. Not inherited: a derived handler declares its own key.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A declaration is data and validates nothing when it is read: the composition gate calls
    ///         <see cref="FindProblems" /> and reports every problem, naming the handler. Beyond what
    ///         <see cref="FindProblems" /> checks, the gate refuses the <see cref="JobHandlerKeys.PlatformPrefix" />
    ///         outside the platform, a key registered twice, and a result entity that is not a queryable
    ///         entity with an <see cref="int" /> property of the given name.
    ///     </para>
    ///     <para>
    ///         Throughput handlers (an outbox, a sweep) declare a batch size above one and run every job
    ///         as the system user; handlers of user-requested work keep the batch size of one.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>[JobHandler("acme.recompute-balances", LeaseSeconds = 600, MaxAttempts = 3, RunsWhenReadOnly = true)]</code>
    /// </example>
    /// <param name="key">The handler key persisted on every job row it runs, in the handler key grammar.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class JobHandlerAttribute(string key) : Attribute
    {
        /// <summary>The handler key persisted on every job row it runs, in the handler key grammar.</summary>
        public string Key { get; } = key;

        /// <summary>The most jobs one claim leases for the handler, from 1 to <see cref="JobLimits.MaxBatchSize" />; 1 by default.</summary>
        public int BatchSize { get; set; } = 1;

        /// <summary>
        ///     The sliding lease's length in seconds, at least <see cref="JobLimits.MinLeaseSeconds" />;
        ///     300 by default. The lease is renewed every fifth of its length while the handler runs.
        /// </summary>
        public int LeaseSeconds { get; set; } = 300;

        /// <summary>
        ///     The claims after which a job that has not succeeded is failed as exhausted, at least 1; 5
        ///     by default.
        /// </summary>
        public int MaxAttempts { get; set; } = 5;

        /// <summary>The most batches of the handler's key in flight per tenant on one instance, at least 1; 1 by default.</summary>
        public int MaxConcurrency { get; set; } = 1;

        /// <summary>The base of the retry backoff in seconds, at least 1; 30 by default.</summary>
        /// <remarks>
        ///     A retry without a delay of its own waits a uniformly random time up to
        ///     <c>min(RetryMaxSeconds, RetryBaseSeconds × 2^(Attempts − 1))</c> seconds.
        /// </remarks>
        public int RetryBaseSeconds { get; set; } = 30;

        /// <summary>The ceiling of the retry backoff in seconds, at least <see cref="RetryBaseSeconds" />; 3,600 by default.</summary>
        public int RetryMaxSeconds { get; set; } = 3600;

        /// <summary>Whether a user-created schedule may name the key; built-in schedules are exempt. False by default.</summary>
        public bool Schedulable { get; set; }

        /// <summary>
        ///     Whether the notice of a failed job reaches the inbox only and no other channel; a handler on
        ///     a delivery path sets it, so a notice of its own failure never re-enters that path. False by
        ///     default.
        /// </summary>
        public bool InboxOnlyFailureNotices { get; set; }

        /// <summary>
        ///     Whether a run commits its work in several transactions, so a cancellation keeps what
        ///     already committed and the client asks before stopping it. False by default.
        /// </summary>
        public bool CommitsInSteps { get; set; }

        /// <summary>
        ///     Whether the worker runs the handler on a read-only tenant: set it when the handler's writes
        ///     are bookkeeping or a read the customer keeps under a soft lock (an export, a delivery, a
        ///     sweep), and leave it off when the handler creates business data. False by default.
        /// </summary>
        public bool RunsWhenReadOnly { get; set; }

        /// <summary>
        ///     The entity whose row a succeeded job leaves for the user, by its entity name
        ///     (<c>core.Export</c>); set together with <see cref="ResultVia" />, or neither. Null by
        ///     default.
        /// </summary>
        public string? ResultEntity { get; set; }

        /// <summary>
        ///     The <see cref="int" /> property of <see cref="ResultEntity" /> that holds the job's id
        ///     (<c>JobId</c>); set together with <see cref="ResultEntity" />, or neither. Null by default.
        /// </summary>
        public string? ResultVia { get; set; }

        /// <summary>
        ///     The problems of the declaration on its own: a key outside the grammar, an option outside
        ///     its range, and one of the result pair without the other. Each is one sentence naming the
        ///     option, for the composition gate to attribute to the handler.
        /// </summary>
        /// <returns>The problems, in declaration order; empty when the declaration is sound.</returns>
        public IReadOnlyList<string> FindProblems()
        {
            List<string> problems = [];

            // --- The key ---
            if (!JobHandlerKeys.IsValid(Key))
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The key '{Key}' is not two or more dot-separated segments of lowercase letters, digits and hyphens, each starting with a letter, of at most {JobLimits.MaxKeyLength} characters."));
            }

            // --- The numeric options, each against its range ---
            if (BatchSize is < 1 or > JobLimits.MaxBatchSize)
            {
                problems.Add(OutOfRange(nameof(BatchSize), BatchSize, 1, JobLimits.MaxBatchSize));
            }

            if (LeaseSeconds < JobLimits.MinLeaseSeconds)
            {
                problems.Add(OutOfRange(nameof(LeaseSeconds), LeaseSeconds, JobLimits.MinLeaseSeconds));
            }

            if (MaxAttempts < 1)
            {
                problems.Add(OutOfRange(nameof(MaxAttempts), MaxAttempts, 1));
            }

            if (MaxConcurrency < 1)
            {
                problems.Add(OutOfRange(nameof(MaxConcurrency), MaxConcurrency, 1));
            }

            if (RetryBaseSeconds < 1)
            {
                problems.Add(OutOfRange(nameof(RetryBaseSeconds), RetryBaseSeconds, 1));
            }

            // The ceiling is held to the base only when the base itself is sound, so one bad value
            // reports once.
            if (RetryMaxSeconds < 1)
            {
                problems.Add(OutOfRange(nameof(RetryMaxSeconds), RetryMaxSeconds, 1));
            }
            else if (RetryBaseSeconds >= 1 && RetryMaxSeconds < RetryBaseSeconds)
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{nameof(RetryMaxSeconds)} is {RetryMaxSeconds}; it must be at least {nameof(RetryBaseSeconds)}, which is {RetryBaseSeconds}."));
            }

            // --- The result pair: both or neither, and neither blank ---
            bool hasEntity = !string.IsNullOrWhiteSpace(ResultEntity);
            bool hasVia = !string.IsNullOrWhiteSpace(ResultVia);
            if (hasEntity != hasVia || (ResultEntity is not null && !hasEntity) || (ResultVia is not null && !hasVia))
            {
                problems.Add($"{nameof(ResultEntity)} and {nameof(ResultVia)} are set together, each to a name, or neither is set.");
            }

            return problems;
        }

        /// <summary>Describes an option outside its range.</summary>
        /// <param name="option">The option's name.</param>
        /// <param name="value">Its value.</param>
        /// <param name="min">The least it may be.</param>
        /// <param name="max">The most it may be; null when unbounded above.</param>
        /// <returns>The problem.</returns>
        private static string OutOfRange(string option, int value, int min, int? max = null)
        {
            return max is int most
                ? string.Create(CultureInfo.InvariantCulture, $"{option} is {value}; it must be from {min} to {most}.")
                : string.Create(CultureInfo.InvariantCulture, $"{option} is {value}; it must be at least {min}.");
        }
    }
}
