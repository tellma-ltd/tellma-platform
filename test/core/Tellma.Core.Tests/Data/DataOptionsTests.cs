// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Data;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data
{
    /// <summary>The data layer's execution settings: their validation, directly and through the composition.</summary>
    public sealed class DataOptionsTests
    {
        [Fact]
        public void The_defaults_are_valid_and_zero_retries_are_too()
        {
            Assert.Empty(Problems(new DataOptions()));
            Assert.Empty(Problems(new DataOptions { RetryAttempts = 0 }));
        }

        [Fact]
        public void A_duration_or_count_that_is_not_positive_and_a_negative_retry_count_are_refused()
        {
            DataOptions options = new()
            {
                CommandTimeout = TimeSpan.Zero,
                MaintenanceCommandTimeout = TimeSpan.FromSeconds(-1),
                RetryBaseDelay = TimeSpan.Zero,
                SlowRoundTripThreshold = TimeSpan.Zero,
                RetryAttempts = -1,
                IdBufferLowWater = 0,
                LargeBatchThreshold = 0,
                ConflictIdsInMessage = -5,
            };

            Assert.Equal(
                [
                    nameof(DataOptions.CommandTimeout),
                    nameof(DataOptions.MaintenanceCommandTimeout),
                    nameof(DataOptions.RetryBaseDelay),
                    nameof(DataOptions.SlowRoundTripThreshold),
                    nameof(DataOptions.RetryAttempts),
                    nameof(DataOptions.IdBufferLowWater),
                    nameof(DataOptions.LargeBatchThreshold),
                    nameof(DataOptions.ConflictIdsInMessage),
                ],
                Problems(options).SelectMany(static problem => problem.MemberNames));
        }

        [Fact]
        public void A_zero_timeout_bound_from_configuration_fails_when_the_options_are_resolved()
        {
            using ServiceProvider provider = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Data:CommandTimeout", "00:00:00"), ("Tellma:Data:RetryAttempts", "-1"))))
                .BuildServiceProvider();

            OptionsValidationException refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<DataOptions>>().Value);
            Assert.Equal(2, refused.Failures.Count());
            Assert.Contains(refused.Failures, static failure => failure.Contains("Tellma:Data:CommandTimeout", StringComparison.Ordinal));
            Assert.Contains(refused.Failures, static failure => failure.Contains("Tellma:Data:RetryAttempts", StringComparison.Ordinal));
        }

        /// <summary>Validates the options as the startup gate does.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The problems found.</returns>
        private static List<ValidationResult> Problems(DataOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
