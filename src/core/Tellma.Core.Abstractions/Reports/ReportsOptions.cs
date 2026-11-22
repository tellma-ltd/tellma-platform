// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.Reports
{
    /// <summary>
    ///     The limits of report runs, bound from <c>Tellma:Reports</c> and validated by the startup
    ///     gate: a value outside its range fails startup. A distribution changes a limit in
    ///     configuration, never in code.
    /// </summary>
    public sealed class ReportsOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Reports";

        /// <summary>The least <see cref="CommandTimeout" /> may be: one second.</summary>
        public static readonly TimeSpan MinCommandTimeout = TimeSpan.FromSeconds(1);

        /// <summary>The most <see cref="CommandTimeout" /> may be: one hour.</summary>
        public static readonly TimeSpan MaxCommandTimeout = TimeSpan.FromHours(1);

        /// <summary>The rows a list or summary page holds when the request names no page size; 200 by default, from 1 to 100,000.</summary>
        [Range(1, 100_000)]
        public int DefaultTake { get; set; } = 200;

        /// <summary>The most rows a list or summary page may hold; 10,000 by default, from 1 to 100,000.</summary>
        [Range(1, 100_000)]
        public int MaxTake { get; set; } = 10_000;

        /// <summary>The most points a chart plots; 10,000 by default, from 1 to 1,000,000.</summary>
        [Range(1, 1_000_000)]
        public int MaxChartPoints { get; set; } = 10_000;

        /// <summary>The most leaf rows a pivot reads; 250,000 by default, from 1 to 10,000,000.</summary>
        [Range(1, 10_000_000)]
        public int MaxPivotRows { get; set; } = 250_000;

        /// <summary>The most row members a pivot shows; 20,000 by default, from 1 to 1,000,000.</summary>
        [Range(1, 1_000_000)]
        public int MaxPivotRowMembers { get; set; } = 20_000;

        /// <summary>The most column members a pivot shows; 2,000 by default, from 1 to 100,000.</summary>
        [Range(1, 100_000)]
        public int MaxPivotColumnMembers { get; set; } = 2_000;

        /// <summary>The most cells a pivot shows; 500,000 by default, from 1 to 10,000,000.</summary>
        [Range(1, 10_000_000)]
        public int MaxPivotCells { get; set; } = 500_000;

        /// <summary>The most cells times partials a pivot accumulates; 5,000,000 by default, from 1 to 100,000,000.</summary>
        [Range(1, 100_000_000)]
        public int MaxPivotCellPartials { get; set; } = 5_000_000;

        /// <summary>How long one run's statement may take; 120 seconds by default, from one second to one hour.</summary>
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>The most compiled plans the process caches; 512 by default, from 1 to 100,000.</summary>
        [Range(1, 100_000)]
        public int PlanCacheSize { get; set; } = 512;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The default page fits the largest one ---
            if (DefaultTake > MaxTake)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(DefaultTake)} is {DefaultTake}, above {SectionPath}:{nameof(MaxTake)} ({MaxTake}); the default page must fit the largest.",
                    [nameof(DefaultTake), nameof(MaxTake)]);
            }

            // --- The command timeout: at least a second, at most an hour ---
            if (CommandTimeout < MinCommandTimeout || CommandTimeout > MaxCommandTimeout)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(CommandTimeout)} is {CommandTimeout}; it must be at least {MinCommandTimeout.TotalSeconds} second and at most {MaxCommandTimeout.TotalMinutes} minutes.",
                    [nameof(CommandTimeout)]);
            }
        }
    }
}
