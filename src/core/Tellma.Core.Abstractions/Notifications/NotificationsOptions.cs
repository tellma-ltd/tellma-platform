// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     The limits of notifications, the inbox and their retention, bound from
    ///     <c>Tellma:Notifications</c> and validated by the startup gate. A distribution changes a
    ///     limit in configuration, never in code.
    /// </summary>
    /// <remarks>
    ///     Every count is at least 1. <see cref="SummaryCountCap" /> is at least 100 because the inbox
    ///     badge reads "99+" from 100, and each other upper bound is the width of what the limit
    ///     governs: the arguments column, the retention statement's page, a delivery job's slice.
    /// </remarks>
    public sealed class NotificationsOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Notifications";

        /// <summary>The most a retention period may be: the database's date arithmetic counts it in seconds, as a 32-bit integer.</summary>
        private static readonly TimeSpan MaxRetention = TimeSpan.FromSeconds(int.MaxValue);

        /// <summary>
        ///     The most recipients one request or one withdrawal may name; 10,000 by default. A request
        ///     beyond it is a programming error, never split automatically.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int MaxRecipientsPerRequest { get; set; } = 10000;

        /// <summary>The most characters a request's serialised arguments may take; 4,000 by default, which is also the most allowed.</summary>
        [Range(1, 4000)]
        public int MaxArgumentsChars { get; set; } = 4000;

        /// <summary>Where the inbox summary stops counting unseen and unread rows; 100 by default, from 100 to 1,000.</summary>
        [Range(100, 1000)]
        public int SummaryCountCap { get; set; } = 100;

        /// <summary>How many of the latest rows, and of the caller's running jobs, the inbox summary carries; 10 by default, at most 50.</summary>
        [Range(1, 50)]
        public int SummaryLatestCount { get; set; } = 10;

        /// <summary>How long a read row is kept after it was read; 90 days by default.</summary>
        public TimeSpan ReadRetention { get; set; } = TimeSpan.FromDays(90);

        /// <summary>How long an unread row is kept after it was created; 365 days by default.</summary>
        public TimeSpan UnreadRetention { get; set; } = TimeSpan.FromDays(365);

        /// <summary>The most rows one retention statement deletes; 1,000 by default, at most 10,000.</summary>
        [Range(1, 10000)]
        public int RetentionPageSize { get; set; } = 1000;

        /// <summary>
        ///     The most recipients one delivery job carries to the channels other than the inbox; 250
        ///     by default, which is also the most allowed.
        /// </summary>
        [Range(1, 250)]
        public int DeliverySliceSize { get; set; } = 250;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- Retention periods: at least a second, and within the statement's 32-bit seconds ---
            foreach ((string name, TimeSpan value) in new[]
            {
                (nameof(ReadRetention), ReadRetention),
                (nameof(UnreadRetention), UnreadRetention),
            })
            {
                if (value < TimeSpan.FromSeconds(1) || value > MaxRetention)
                {
                    yield return new ValidationResult(
                        $"{SectionPath}:{name} is {value}; it must be at least one second and at most {MaxRetention.Days} days.",
                        [name]);
                }
            }
        }
    }
}
