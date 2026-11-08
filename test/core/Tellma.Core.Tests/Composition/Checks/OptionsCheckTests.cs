// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.options</c>: declared options are validated at start, one problem per failure.</summary>
    public class OptionsCheckTests
    {
        private const string Check = "core.options";

        [Fact]
        public async Task Valid_options_pass()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(new Dictionary<string, string?>
            {
                ["Tellma:Acme:Name"] = "Acme",
                ["Tellma:Acme:Count"] = "10",
            });

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task Each_data_annotation_failure_is_its_own_problem_naming_the_options_path_and_feature()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(new Dictionary<string, string?>
            {
                ["Tellma:Acme:Count"] = "11",
            });

            List<CompositionProblem> options = problems.From(Check);
            Assert.Equal(2, options.Count);
            Assert.All(options, static problem =>
            {
                Assert.Contains("bound from 'Tellma:Acme' by 'acme'", problem.Problem, StringComparison.Ordinal);
                Assert.Contains("'Tellma:Acme'", problem.Fix, StringComparison.Ordinal);
            });
            Assert.Contains(options, static problem => problem.Problem.Contains(nameof(SampleOptions.Name), StringComparison.Ordinal));
            Assert.Contains(options, static problem => problem.Problem.Contains(nameof(SampleOptions.Count), StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_value_that_does_not_convert_is_a_problem_not_a_crash()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(new Dictionary<string, string?>
            {
                ["Tellma:Acme:Name"] = "Acme",
                ["Tellma:Acme:Count"] = "many",
            });

            Assert.Contains("many", Assert.Single(problems.From(Check)).Problem, StringComparison.Ordinal);
        }

        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(Dictionary<string, string?> values)
        {
            values["Tellma:PublicOrigin"] = "https://acme.app.tellma.com";
            return TestComposition.CheckAsync(
                static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", declare: static d => d.Options<SampleOptions>("Tellma:Acme"))),
                TestComposition.Configuration(values));
        }
    }
}
