// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Abstractions.Tests.Jobs
{
    /// <summary>How a handler is declared: the key grammar, the label key, the attribute's defaults and the ranges it reports.</summary>
    public class JobHandlerDeclarationTests
    {
        [Theory]
        [InlineData("core.blob-sweep")]
        [InlineData("acme.export.v2")]
        [InlineData("a.b")]
        [InlineData("core.email-outbox.send")]
        public void A_key_of_two_or_more_lowercase_segments_is_valid(string key)
        {
            Assert.True(JobHandlerKeys.IsValid(key));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("core")]
        [InlineData("Core.export")]
        [InlineData("core.Export")]
        [InlineData("core..export")]
        [InlineData(".core.export")]
        [InlineData("core.export.")]
        [InlineData("core.1export")]
        [InlineData("core.-export")]
        [InlineData("core.export_all")]
        [InlineData("core. export")]
        [InlineData("córe.export")]
        public void A_key_outside_the_grammar_is_invalid(string? key)
        {
            Assert.False(JobHandlerKeys.IsValid(key));
        }

        [Fact]
        public void A_key_has_at_most_sixty_four_characters()
        {
            Assert.True(JobHandlerKeys.IsValid("a." + new string('b', 62)));
            Assert.False(JobHandlerKeys.IsValid("a." + new string('b', 63)));
        }

        [Fact]
        public void The_platform_prefix_is_core_and_compared_ordinally()
        {
            Assert.True(JobHandlerKeys.IsPlatformKey("core.blob-sweep"));
            Assert.False(JobHandlerKeys.IsPlatformKey("acme.core.export"));
            Assert.False(JobHandlerKeys.IsPlatformKey("corex.export"));
            _ = Assert.Throws<ArgumentNullException>(static () => JobHandlerKeys.IsPlatformKey(null!));
        }

        [Fact]
        public void A_label_key_replaces_every_dot_of_the_handler_key_with_an_underscore()
        {
            Assert.Equal("JobHandler_core_blob-sweep", JobHandlerKeys.LabelKeyOf("core.blob-sweep"));
            Assert.Equal("JobHandler_acme_export_v2", JobHandlerKeys.LabelKeyOf("acme.export.v2"));
        }

        [Fact]
        public void The_attribute_is_one_per_class_and_not_inherited()
        {
            AttributeUsageAttribute usage = typeof(JobHandlerAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal((AttributeTargets.Class, false, false), (usage.ValidOn, usage.AllowMultiple, usage.Inherited));
        }

        [Fact]
        public void The_attribute_defaults_are_the_documented_ones_and_sound()
        {
            JobHandlerAttribute attribute = new("core.blob-sweep");

            Assert.Equal(
                ("core.blob-sweep", 1, 300, 5, 1, 30, 3600),
                (attribute.Key, attribute.BatchSize, attribute.LeaseSeconds, attribute.MaxAttempts, attribute.MaxConcurrency,
                    attribute.RetryBaseSeconds, attribute.RetryMaxSeconds));
            Assert.Equal(
                (false, false, false, false, null, null),
                (attribute.Schedulable, attribute.InboxOnlyFailureNotices, attribute.CommitsInSteps, attribute.RunsWhenReadOnly,
                    attribute.ResultEntity, attribute.ResultVia));
            Assert.Empty(attribute.FindProblems());
        }

        [Fact]
        public void An_invalid_key_is_reported()
        {
            string problem = Assert.Single(new JobHandlerAttribute("Core.Export").FindProblems());

            Assert.Contains("'Core.Export'", problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(nameof(JobHandlerAttribute.BatchSize), 1, 0, 500, 501)]
        [InlineData(nameof(JobHandlerAttribute.LeaseSeconds), 15, 14, int.MaxValue, null)]
        [InlineData(nameof(JobHandlerAttribute.MaxAttempts), 1, 0, int.MaxValue, null)]
        [InlineData(nameof(JobHandlerAttribute.MaxConcurrency), 1, 0, int.MaxValue, null)]
        [InlineData(nameof(JobHandlerAttribute.RetryBaseSeconds), 1, 0, 3600, null)]
        [InlineData(nameof(JobHandlerAttribute.RetryMaxSeconds), 30, 0, int.MaxValue, null)]
        public void Each_option_accepts_its_bounds_and_reports_beyond_them(string option, int lowest, int below, int highest, int? above)
        {
            Assert.Empty(With(option, lowest).FindProblems());
            Assert.Empty(With(option, highest).FindProblems());
            Assert.StartsWith(option + " is ", Assert.Single(With(option, below).FindProblems()), StringComparison.Ordinal);
            if (above is int beyond)
            {
                Assert.StartsWith(option + " is ", Assert.Single(With(option, beyond).FindProblems()), StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_retry_ceiling_is_at_least_the_base_and_reports_once_when_the_base_is_unsound()
        {
            string problem = Assert.Single(new JobHandlerAttribute("core.export") { RetryBaseSeconds = 60, RetryMaxSeconds = 59 }.FindProblems());
            Assert.Contains("at least RetryBaseSeconds", problem, StringComparison.Ordinal);

            // An unsound base reports alone; the ceiling is not held to it.
            Assert.StartsWith(
                "RetryBaseSeconds is 0",
                Assert.Single(new JobHandlerAttribute("core.export") { RetryBaseSeconds = 0, RetryMaxSeconds = 1 }.FindProblems()),
                StringComparison.Ordinal);
        }

        [Fact]
        public void Every_problem_of_one_declaration_is_reported_at_once()
        {
            JobHandlerAttribute attribute = new("export") { BatchSize = 0, LeaseSeconds = 1, MaxAttempts = 0, ResultEntity = "core.Export" };

            Assert.Equal(5, attribute.FindProblems().Count);
        }

        [Theory]
        [InlineData("core.Export", "JobId", true)]
        [InlineData(null, null, true)]
        [InlineData("core.Export", null, false)]
        [InlineData(null, "JobId", false)]
        [InlineData(" ", "JobId", false)]
        [InlineData("core.Export", "", false)]
        public void The_result_entity_and_its_property_are_set_together_or_not_at_all(string? entity, string? via, bool sound)
        {
            JobHandlerAttribute attribute = new("core.export") { ResultEntity = entity, ResultVia = via };

            Assert.Equal(sound, attribute.FindProblems().Count == 0);
        }

        /// <summary>A sound declaration with one numeric option changed.</summary>
        /// <param name="option">The option's name.</param>
        /// <param name="value">Its value.</param>
        /// <returns>The declaration.</returns>
        private static JobHandlerAttribute With(string option, int value)
        {
            JobHandlerAttribute attribute = new("core.export");
            typeof(JobHandlerAttribute).GetProperty(option)!.SetValue(attribute, value);
            return attribute;
        }
    }
}
