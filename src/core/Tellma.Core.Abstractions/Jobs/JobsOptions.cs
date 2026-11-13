// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The settings of the job worker, the scheduler and job retention, bound from
    ///     <c>Tellma:Jobs</c> and validated by the startup gate. A deployment changes them in
    ///     configuration, never in code.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The bounds: <see cref="MaxParallelBatches" /> from 1 to 256; one second at most
    ///         <see cref="MinPollInterval" />, at most <see cref="MaxPollInterval" />, at most 30 seconds,
    ///         the cron granularity; <see cref="DrainTimeout" /> at least a second; <see cref="GapThreshold" />
    ///         at least an hour and at most 100 years, a span the gap check can subtract from any
    ///         plausible now without leaving the date range; each retention at least a day and at most
    ///         what the retention statements' 32-bit seconds can carry.
    ///     </para>
    ///     <para>
    ///         <see cref="DrainTimeout" /> must also stay below the host's shutdown timeout, which bounds
    ///         the final lease renewal at shutdown; the worker's host holds it to that, since only the
    ///         host knows its own timeout.
    ///     </para>
    /// </remarks>
    public sealed class JobsOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Jobs";

        /// <summary>The shortest a poll interval may be.</summary>
        private static readonly TimeSpan PollFloor = TimeSpan.FromSeconds(1);

        /// <summary>The longest a poll interval may be: the cron granularity of the scheduler, which rides the poll.</summary>
        private static readonly TimeSpan PollCeiling = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     The longest the gap threshold may be, 100 years: the gap check subtracts it from the
        ///     database's clock, and a span reaching before year 1 would fail every poll.
        /// </summary>
        private static readonly TimeSpan MaxGapThreshold = TimeSpan.FromDays(36_500);

        /// <summary>The longest a retention period may be: the retention statements bind it in seconds, as a 32-bit integer.</summary>
        private static readonly TimeSpan MaxRetention = TimeSpan.FromSeconds(int.MaxValue);

        /// <summary>
        ///     Whether this host runs the job worker and the scheduler; true by default. Off, enqueueing,
        ///     the job and schedule stacks and the schedule service still work, and another instance of
        ///     the deployment runs the jobs. The migrator's host always turns it off.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The most job batches this instance runs at once across every tenant; 8 by default, from 1 to 256.</summary>
        [Range(1, 256)]
        public int MaxParallelBatches { get; set; } = 8;

        /// <summary>
        ///     The shortest wait between two polls of one tenant, after a poll that found work; one
        ///     second by default, at least one second and at most <see cref="MaxPollInterval" />.
        /// </summary>
        public TimeSpan MinPollInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        ///     The longest wait between two polls of one tenant, which idle polls double towards; 30
        ///     seconds by default, at most 30. Every job is picked up within it, nudged or not.
        /// </summary>
        public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     How long a stopping host waits for running handlers to return before it leaves them
        ///     leased and stops; four seconds by default, at least one second.
        /// </summary>
        public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(4);

        /// <summary>
        ///     How long the worker may have been away before queued work it finds overdue by more than
        ///     this is held for an administrator rather than run, as after a restored backup; six hours
        ///     by default, at least one hour and at most 100 years.
        /// </summary>
        public TimeSpan GapThreshold { get; set; } = TimeSpan.FromHours(6);

        /// <summary>How long a succeeded job is kept after it ended; 30 days by default, at least one day.</summary>
        public TimeSpan SucceededRetention { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        ///     How long a failed or cancelled job is kept after it ended, and how long a job may wait for
        ///     confirmation before retention stops it; 180 days by default, at least one day.
        /// </summary>
        public TimeSpan FailedRetention { get; set; } = TimeSpan.FromDays(180);

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The poll intervals: 1 s ≤ min ≤ max ≤ 30 s ---
            bool minInRange = MinPollInterval >= PollFloor && MinPollInterval <= PollCeiling;
            if (!minInRange)
            {
                yield return Problem(nameof(MinPollInterval), MinPollInterval, "at least one second and at most 30 seconds");
            }

            if (MaxPollInterval < PollFloor || MaxPollInterval > PollCeiling)
            {
                yield return Problem(nameof(MaxPollInterval), MaxPollInterval, "at least one second and at most 30 seconds");
            }
            else if (minInRange && MaxPollInterval < MinPollInterval)
            {
                // Held to the minimum only when the minimum is itself in range, so one bad value
                // reports once.
                yield return Problem(nameof(MaxPollInterval), MaxPollInterval, $"at least {nameof(MinPollInterval)}");
            }

            // --- The drain: at least a second (the host's own timeout is the upper bound) ---
            if (DrainTimeout < TimeSpan.FromSeconds(1))
            {
                yield return Problem(nameof(DrainTimeout), DrainTimeout, "at least one second");
            }

            // --- The gap threshold: at least an hour, at most a span the gap check can subtract ---
            if (GapThreshold < TimeSpan.FromHours(1) || GapThreshold > MaxGapThreshold)
            {
                yield return Problem(
                    nameof(GapThreshold),
                    GapThreshold,
                    string.Create(CultureInfo.InvariantCulture, $"at least one hour and at most {MaxGapThreshold.Days} days"));
            }

            // --- The retention periods: at least a day, within the statements' 32-bit seconds ---
            foreach ((string name, TimeSpan value) in new[]
            {
                (nameof(SucceededRetention), SucceededRetention),
                (nameof(FailedRetention), FailedRetention),
            })
            {
                if (value < TimeSpan.FromDays(1) || value > MaxRetention)
                {
                    yield return Problem(
                        name,
                        value,
                        string.Create(CultureInfo.InvariantCulture, $"at least one day and at most {MaxRetention.Days} days"));
                }
            }
        }

        /// <summary>Describes a value outside its bounds, naming its configuration key.</summary>
        /// <param name="name">The option's name.</param>
        /// <param name="value">Its value.</param>
        /// <param name="bounds">The bounds it must fall within, as words.</param>
        /// <returns>The result, naming the option as its member.</returns>
        private static ValidationResult Problem(string name, TimeSpan value, string bounds)
        {
            return new ValidationResult(
                string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{name} is {value:c}; it must be {bounds}."),
                [name]);
        }
    }
}
