// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     The data layer's execution settings, bound from <c>Tellma:Data</c> and validated by the
    ///     startup gate.
    /// </summary>
    public sealed class DataOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Data";

        /// <summary>The command timeout of read, validation and persist round trips; 30 seconds by default.</summary>
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>The command timeout of maintenance round trips; 600 seconds by default.</summary>
        public TimeSpan MaintenanceCommandTimeout { get; set; } = TimeSpan.FromSeconds(600);

        /// <summary>The re-runs after the first attempt of a round trip that failed transiently; 3 by default, four attempts in all.</summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>The first retry delay, quadrupled for each later one, with ±25 % jitter; 50 ms by default.</summary>
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>A round trip slower than this logs its full text; 5 seconds by default.</summary>
        public TimeSpan SlowRoundTripThreshold { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>The id buffer refills when fewer ids than this remain; 64 by default.</summary>
        public int IdBufferLowWater { get; set; } = 64;

        /// <summary>A save of more rows than this is a large batch, compiled per execution; 1,000 by default.</summary>
        public int LargeBatchThreshold { get; set; } = 1000;

        /// <summary>The most ids a concurrency-conflict message lists per id list; 100 by default.</summary>
        public int ConflictIdsInMessage { get; set; } = 100;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- Durations: each must be positive ---
            foreach ((string name, TimeSpan value) in new[]
            {
                (nameof(CommandTimeout), CommandTimeout),
                (nameof(MaintenanceCommandTimeout), MaintenanceCommandTimeout),
                (nameof(RetryBaseDelay), RetryBaseDelay),
                (nameof(SlowRoundTripThreshold), SlowRoundTripThreshold),
            })
            {
                if (value <= TimeSpan.Zero)
                {
                    yield return new ValidationResult($"{SectionPath}:{name} is {value}; it must be positive.", [name]);
                }
            }

            // --- Counts: the retry count may be zero, the rest must be positive ---
            if (RetryAttempts < 0)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(RetryAttempts)} is {RetryAttempts}; it must be zero or more.", [nameof(RetryAttempts)]);
            }

            foreach ((string name, int value) in new[]
            {
                (nameof(IdBufferLowWater), IdBufferLowWater),
                (nameof(LargeBatchThreshold), LargeBatchThreshold),
                (nameof(ConflictIdsInMessage), ConflictIdsInMessage),
            })
            {
                if (value <= 0)
                {
                    yield return new ValidationResult($"{SectionPath}:{name} is {value}; it must be positive.", [name]);
                }
            }
        }
    }
}
