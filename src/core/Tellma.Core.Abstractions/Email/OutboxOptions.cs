// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The limits of the email outbox, bound from <c>Tellma:Outbox</c> and validated by the startup
    ///     gate: a value outside its range fails startup. A distribution changes a limit in
    ///     configuration, never in code.
    /// </summary>
    /// <remarks>
    ///     The defaults are Azure Communication Services' caps, which every transport accepts; the upper
    ///     bounds are SendGrid's, whose 30 MB cap on the encoded message is about 20 MiB before base64.
    /// </remarks>
    public sealed class OutboxOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Outbox";

        /// <summary>The least <see cref="MaxMessageBytes" /> may be: one KiB.</summary>
        public const long MinMessageBytes = 1024;

        /// <summary>The most <see cref="MaxMessageBytes" /> may be: 20 MiB.</summary>
        public const long MaxMessageBytesLimit = 20L * 1024 * 1024;

        /// <summary>The most <see cref="MaxRecipients" /> may be.</summary>
        public const int MaxRecipientsLimit = 1000;

        /// <summary>The least <see cref="Retention" /> may be: one day.</summary>
        public static readonly TimeSpan MinRetention = TimeSpan.FromDays(1);

        /// <summary>The most <see cref="Retention" /> may be: ten years of 365 days.</summary>
        public static readonly TimeSpan MaxRetention = TimeSpan.FromDays(3650);

        /// <summary>How long a row is kept after it was created; 180 days by default, from one day to ten years.</summary>
        public TimeSpan Retention { get; set; } = TimeSpan.FromDays(180);

        /// <summary>The most bytes one email's bodies and attachments may take together; 7 MiB by default, from 1 KiB to 20 MiB.</summary>
        [Range(MinMessageBytes, MaxMessageBytesLimit)]
        public long MaxMessageBytes { get; set; } = 7L * 1024 * 1024;

        /// <summary>The most addresses one email's To, Cc and Bcc may hold together; 50 by default, from 1 to 1,000.</summary>
        [Range(1, MaxRecipientsLimit)]
        public int MaxRecipients { get; set; } = 50;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The retention period: at least a day, at most ten years ---
            if (Retention < MinRetention || Retention > MaxRetention)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(Retention)} is {Retention}; it must be at least {MinRetention.Days} day and at most {MaxRetention.Days} days.",
                    [nameof(Retention)]);
            }
        }
    }
}
