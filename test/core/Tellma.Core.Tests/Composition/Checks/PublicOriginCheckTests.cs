// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.public-origin</c>: an absolute, path-less origin, https outside Development.</summary>
    public class PublicOriginCheckTests
    {
        private const string Check = "core.public-origin";

        [Theory]
        [InlineData(null, "Production", "is not set")]
        [InlineData("", "Production", "is not set")]
        [InlineData("acme.app.tellma.com", "Production", "not an absolute URI")]
        [InlineData("/acme", "Production", "not an absolute URI")]
        [InlineData("http://acme.app.tellma.com", "Production", "only Development may serve over http")]
        [InlineData("http://acme.app.tellma.com", "Staging", "only Development may serve over http")]
        [InlineData("ftp://acme.app.tellma.com", "Development", "neither https nor http")]
        [InlineData("https://acme.app.tellma.com/", "Production", "carries more than an origin")]
        [InlineData("https://acme.app.tellma.com/app", "Production", "carries more than an origin")]
        [InlineData("https://acme.app.tellma.com?x=1", "Production", "carries more than an origin")]
        [InlineData("https://acme.app.tellma.com#top", "Production", "carries more than an origin")]
        [InlineData("https://user@acme.app.tellma.com", "Production", "carries more than an origin")]
        public async Task An_origin_that_is_missing_relative_insecure_or_more_than_an_origin_is_reported(
            string? origin, string environment, string expected)
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(origin, environment);

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Contains(expected, problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Tellma:PublicOrigin", problem.Fix, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("https://acme.app.tellma.com", "Production")]
        [InlineData("https://acme.app.tellma.com:8443", "Production")]
        [InlineData("https://localhost:4200", "Development")]
        [InlineData("http://localhost:4200", "Development")]
        public async Task An_absolute_path_less_origin_passes(string origin, string environment)
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(origin, environment);

            Assert.Empty(problems.From(Check));
        }

        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(string? origin, string environment)
        {
            return TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(new Dictionary<string, string?> { ["Tellma:PublicOrigin"] = origin }),
                new TestEnvironment(environment, Path.GetTempPath()));
        }
    }
}
