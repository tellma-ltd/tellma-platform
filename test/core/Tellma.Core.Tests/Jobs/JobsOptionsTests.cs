// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>The settings of the job worker and the scheduler: their defaults and bounds, directly and through the composition's options check.</summary>
    public class JobsOptionsTests
    {
        [Fact]
        public void The_defaults_are_the_documented_ones_and_valid()
        {
            JobsOptions options = new();

            Assert.Equal(
                (true, 8, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(4), TimeSpan.FromHours(6), TimeSpan.FromDays(30), TimeSpan.FromDays(180)),
                (options.Enabled, options.MaxParallelBatches, options.MinPollInterval, options.MaxPollInterval, options.DrainTimeout,
                    options.GapThreshold, options.SucceededRetention, options.FailedRetention));
            Assert.Equal("Tellma:Jobs", JobsOptions.SectionPath);
            Assert.Empty(Problems(options));
        }

        [Fact]
        public void Parallel_batches_are_from_one_to_two_hundred_fifty_six()
        {
            Assert.Empty(Problems(new JobsOptions { MaxParallelBatches = 1 }));
            Assert.Empty(Problems(new JobsOptions { MaxParallelBatches = 256 }));
            Assert.Equal([nameof(JobsOptions.MaxParallelBatches)], Assert.Single(Problems(new JobsOptions { MaxParallelBatches = 0 })).MemberNames);
            Assert.Equal([nameof(JobsOptions.MaxParallelBatches)], Assert.Single(Problems(new JobsOptions { MaxParallelBatches = 257 })).MemberNames);
        }

        [Fact]
        public void The_poll_intervals_are_between_one_and_thirty_seconds_and_ordered()
        {
            Assert.Empty(Problems(new JobsOptions { MinPollInterval = TimeSpan.FromSeconds(1), MaxPollInterval = TimeSpan.FromSeconds(1) }));
            Assert.Empty(Problems(new JobsOptions { MinPollInterval = TimeSpan.FromSeconds(30), MaxPollInterval = TimeSpan.FromSeconds(30) }));

            Assert.Equal([nameof(JobsOptions.MinPollInterval)], MembersOf(new JobsOptions { MinPollInterval = TimeSpan.FromMilliseconds(999) }));
            Assert.Equal([nameof(JobsOptions.MaxPollInterval)], MembersOf(new JobsOptions { MaxPollInterval = TimeSpan.FromSeconds(31) }));
            Assert.Equal(
                [nameof(JobsOptions.MaxPollInterval)],
                MembersOf(new JobsOptions { MinPollInterval = TimeSpan.FromSeconds(10), MaxPollInterval = TimeSpan.FromSeconds(5) }));

            // A minimum out of range reports alone: the maximum is not also held to it.
            Assert.Equal(
                [nameof(JobsOptions.MinPollInterval)],
                MembersOf(new JobsOptions { MinPollInterval = TimeSpan.FromMinutes(1), MaxPollInterval = TimeSpan.FromSeconds(30) }));
        }

        [Fact]
        public void The_drain_timeout_is_at_least_a_second()
        {
            Assert.Empty(Problems(new JobsOptions { DrainTimeout = TimeSpan.FromSeconds(1) }));
            Assert.Equal([nameof(JobsOptions.DrainTimeout)], MembersOf(new JobsOptions { DrainTimeout = TimeSpan.FromMilliseconds(999) }));
        }

        [Fact]
        public void The_gap_threshold_is_at_least_an_hour_and_fits_the_gap_checks_minutes()
        {
            Assert.Empty(Problems(new JobsOptions { GapThreshold = TimeSpan.FromHours(1) }));
            Assert.Empty(Problems(new JobsOptions { GapThreshold = TimeSpan.FromMinutes(int.MaxValue) }));
            Assert.Equal([nameof(JobsOptions.GapThreshold)], MembersOf(new JobsOptions { GapThreshold = TimeSpan.FromMinutes(59) }));
            Assert.Equal([nameof(JobsOptions.GapThreshold)], MembersOf(new JobsOptions { GapThreshold = TimeSpan.FromMinutes(int.MaxValue + 1L) }));
        }

        [Fact]
        public void A_retention_period_is_at_least_a_day_and_fits_the_statements_seconds()
        {
            Assert.Empty(Problems(new JobsOptions { SucceededRetention = TimeSpan.FromDays(1), FailedRetention = TimeSpan.FromSeconds(int.MaxValue) }));

            JobsOptions refused = new() { SucceededRetention = TimeSpan.FromHours(23), FailedRetention = TimeSpan.FromSeconds(int.MaxValue + 1L) };
            List<ValidationResult> problems = Problems(refused);
            Assert.Equal(
                [nameof(JobsOptions.SucceededRetention), nameof(JobsOptions.FailedRetention)],
                problems.SelectMany(static problem => problem.MemberNames));
            Assert.All(problems, static problem => Assert.StartsWith("Tellma:Jobs:", problem.ErrorMessage, StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_composition_binds_the_section_and_its_options_check_reports_a_bound_out_of_range()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Jobs:MaxPollInterval", "00:01:00"))));

            CompositionProblem problem = Assert.Single(problems.From("core.options"));
            Assert.Contains("MaxPollInterval", problem.Problem, StringComparison.Ordinal);

            await using ServiceProvider provider = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Jobs:MaxParallelBatches", "4"), ("Tellma:Jobs:Enabled", "false"))))
                .BuildServiceProvider(validateScopes: true);
            JobsOptions bound = provider.GetRequiredService<IOptions<JobsOptions>>().Value;
            Assert.Equal((4, false), (bound.MaxParallelBatches, bound.Enabled));
        }

        /// <summary>The members the problems of some options name, in order.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The member names.</returns>
        private static List<string> MembersOf(JobsOptions options)
        {
            return [.. Problems(options).SelectMany(static problem => problem.MemberNames)];
        }

        /// <summary>Validates the options as the startup gate does.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The problems found.</returns>
        private static List<ValidationResult> Problems(JobsOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
