// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>The outbox's limits: their defaults and bounds, directly and through the composition's options check.</summary>
    public sealed class OutboxOptionsTests
    {
        [Fact]
        public void The_defaults_are_the_documented_ones_and_valid()
        {
            OutboxOptions options = new();

            Assert.Equal((TimeSpan.FromDays(180), 7L * 1024 * 1024, 50), (options.Retention, options.MaxMessageBytes, options.MaxRecipients));
            Assert.Empty(Problems(options));
        }

        [Theory]
        [InlineData(1024L, true)]
        [InlineData(1023L, false)]
        [InlineData(20L * 1024 * 1024, true)]
        [InlineData((20L * 1024 * 1024) + 1, false)]
        public void The_message_size_limit_is_between_a_kibibyte_and_twenty_mebibytes(long bytes, bool valid)
        {
            List<ValidationResult> problems = Problems(new OutboxOptions { MaxMessageBytes = bytes });

            Assert.Equal(valid ? [] : [nameof(OutboxOptions.MaxMessageBytes)], problems.SelectMany(static problem => problem.MemberNames));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        [InlineData(1000, true)]
        [InlineData(1001, false)]
        public void The_recipient_limit_is_between_one_and_a_thousand(int recipients, bool valid)
        {
            List<ValidationResult> problems = Problems(new OutboxOptions { MaxRecipients = recipients });

            Assert.Equal(valid ? [] : [nameof(OutboxOptions.MaxRecipients)], problems.SelectMany(static problem => problem.MemberNames));
        }

        [Fact]
        public void The_retention_is_at_least_a_day_and_at_most_ten_years()
        {
            Assert.Empty(Problems(new OutboxOptions { Retention = TimeSpan.FromDays(1) }));
            Assert.Empty(Problems(new OutboxOptions { Retention = TimeSpan.FromDays(3650) }));

            foreach (TimeSpan refused in new[] { TimeSpan.FromHours(23), TimeSpan.FromDays(3651) })
            {
                ValidationResult problem = Assert.Single(Problems(new OutboxOptions { Retention = refused }));
                Assert.Equal([nameof(OutboxOptions.Retention)], problem.MemberNames);
                Assert.StartsWith("Tellma:Outbox:Retention", problem.ErrorMessage, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task The_composition_binds_the_section_and_its_options_check_reports_a_bound_out_of_range()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Outbox:MaxRecipients", "0"))));

            CompositionProblem problem = Assert.Single(problems.From("core.options"));
            Assert.Contains("MaxRecipients", problem.Problem, StringComparison.Ordinal);

            await using ServiceProvider provider = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Outbox:MaxRecipients", "20"))))
                .BuildServiceProvider(validateScopes: true);
            Assert.Equal(20, provider.GetRequiredService<IOptions<OutboxOptions>>().Value.MaxRecipients);
        }

        /// <summary>Validates the options as the startup gate does.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The problems found.</returns>
        private static List<ValidationResult> Problems(OutboxOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
