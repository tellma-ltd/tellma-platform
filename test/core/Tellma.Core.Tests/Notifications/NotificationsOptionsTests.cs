// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>The limits of notifications: their defaults and bounds, directly and through the composition's options check.</summary>
    public class NotificationsOptionsTests
    {
        [Fact]
        public void The_defaults_are_the_documented_ones_and_valid()
        {
            NotificationsOptions options = new();

            Assert.Equal(
                (10000, 4000, 100, 10, TimeSpan.FromDays(90), TimeSpan.FromDays(365), 1000, 250),
                (options.MaxRecipientsPerRequest, options.MaxArgumentsChars, options.SummaryCountCap, options.SummaryLatestCount,
                    options.ReadRetention, options.UnreadRetention, options.RetentionPageSize, options.DeliverySliceSize));
            Assert.Empty(Problems(options));
        }

        [Theory]
        [InlineData(nameof(NotificationsOptions.MaxRecipientsPerRequest), 1, 0, int.MaxValue, null)]
        [InlineData(nameof(NotificationsOptions.MaxArgumentsChars), 1, 0, 4000, 4001)]
        [InlineData(nameof(NotificationsOptions.SummaryCountCap), 100, 99, 1000, 1001)]
        [InlineData(nameof(NotificationsOptions.SummaryLatestCount), 1, 0, 50, 51)]
        [InlineData(nameof(NotificationsOptions.RetentionPageSize), 1, 0, 10000, 10001)]
        [InlineData(nameof(NotificationsOptions.DeliverySliceSize), 1, 0, 250, 251)]
        public void Each_count_accepts_its_bounds_and_refuses_beyond_them(string property, int lowest, int below, int highest, int? above)
        {
            Assert.Empty(Problems(With(property, lowest)));
            Assert.Empty(Problems(With(property, highest)));
            Assert.Equal([property], Assert.Single(Problems(With(property, below))).MemberNames);
            if (above is int beyond)
            {
                Assert.Equal([property], Assert.Single(Problems(With(property, beyond))).MemberNames);
            }
        }

        [Fact]
        public void A_retention_period_is_at_least_a_second_and_fits_the_statements_seconds()
        {
            Assert.Empty(Problems(new NotificationsOptions { ReadRetention = TimeSpan.FromSeconds(1), UnreadRetention = TimeSpan.FromSeconds(int.MaxValue) }));

            NotificationsOptions refused = new() { ReadRetention = TimeSpan.Zero, UnreadRetention = TimeSpan.FromSeconds(int.MaxValue + 1L) };
            List<ValidationResult> problems = Problems(refused);
            Assert.Equal(
                [nameof(NotificationsOptions.ReadRetention), nameof(NotificationsOptions.UnreadRetention)],
                problems.SelectMany(static problem => problem.MemberNames));
            Assert.All(problems, static problem => Assert.StartsWith("Tellma:Notifications:", problem.ErrorMessage, StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_composition_binds_the_section_and_its_options_check_reports_a_bound_out_of_range()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Notifications:DeliverySliceSize", "500"))));

            CompositionProblem problem = Assert.Single(problems.From("core.options"));
            Assert.Contains("DeliverySliceSize", problem.Problem, StringComparison.Ordinal);

            await using ServiceProvider provider = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Notifications:SummaryLatestCount", "20"))))
                .BuildServiceProvider(validateScopes: true);
            Assert.Equal(20, provider.GetRequiredService<IOptions<NotificationsOptions>>().Value.SummaryLatestCount);
        }

        /// <summary>The default options with one count changed.</summary>
        /// <param name="property">The count's name.</param>
        /// <param name="value">Its value.</param>
        /// <returns>The options.</returns>
        private static NotificationsOptions With(string property, int value)
        {
            NotificationsOptions options = new();
            typeof(NotificationsOptions).GetProperty(property)!.SetValue(options, value);
            return options;
        }

        /// <summary>Validates the options as the startup gate does.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The problems found.</returns>
        private static List<ValidationResult> Problems(NotificationsOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
