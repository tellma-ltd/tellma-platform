// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tests.Composition
{
    /// <summary>Problems, the exception that carries them, and the deployment versions record.</summary>
    public class CompositionProblemTests
    {
        [Fact]
        public void A_problem_renders_as_one_line_with_its_fix_when_it_has_one()
        {
            Assert.Equal("[core.options] Bad. Fix: Set it.", new CompositionProblem("core.options", "Bad.", "Set it.").Describe());
            Assert.Equal("[core.options] Bad.", new CompositionProblem("core.options", "Bad.", null).Describe());
        }

        [Theory]
        [InlineData(null, "Bad.")]
        [InlineData(" ", "Bad.")]
        [InlineData("core.options", null)]
        [InlineData("core.options", "")]
        public void A_problem_needs_a_source_and_a_description(string? source, string? problem)
        {
            _ = Assert.ThrowsAny<ArgumentException>(() => new CompositionProblem(source!, problem!, null));
        }

        [Fact]
        public void The_exception_lists_a_header_and_one_line_per_problem_with_identical_newlines_everywhere()
        {
            CompositionProblem[] problems =
            [
                new("core.composition", "The slug is bad.", "Use a good slug."),
                new("core.options", "An option is bad.", null),
            ];

            TellmaCompositionException exception = new(problems);

            Assert.Equal(
                "The Tellma composition has 2 problems:\n- [core.composition] The slug is bad. Fix: Use a good slug.\n- [core.options] An option is bad.",
                exception.Message);
            Assert.Equal(problems, exception.Problems);
            Assert.Equal("The Tellma composition has 1 problem:", new TellmaCompositionException([problems[1]]).Message.Split('\n')[0]);
        }

        [Fact]
        public void The_exception_keeps_its_own_copy_of_the_problems()
        {
            List<CompositionProblem> problems = [new("core.options", "Bad.", null)];
            TellmaCompositionException exception = new(problems);

            problems.Clear();

            Assert.Single(exception.Problems);
        }

        [Fact]
        public void The_exception_needs_at_least_one_problem_and_no_null()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new TellmaCompositionException(null!));
            _ = Assert.Throws<ArgumentException>(() => new TellmaCompositionException([]));
            _ = Assert.Throws<ArgumentException>(() => new TellmaCompositionException([null!]));
        }

        [Fact]
        public void Deployment_versions_need_both_versions()
        {
            DeploymentVersions versions = new("1.2.3+abc", "4.5.6+def");

            Assert.Equal(("1.2.3+abc", "4.5.6+def"), (versions.PlatformVersion, versions.DistributionVersion));
            _ = Assert.ThrowsAny<ArgumentException>(() => new DeploymentVersions("", "4.5.6"));
            _ = Assert.ThrowsAny<ArgumentException>(() => new DeploymentVersions("1.2.3", null!));
        }
    }
}
