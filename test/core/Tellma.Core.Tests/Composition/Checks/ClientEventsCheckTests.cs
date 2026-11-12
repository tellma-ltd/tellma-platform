// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.client-events</c>: names follow the notification type key grammar and are unique.</summary>
    public class ClientEventsCheckTests
    {
        private const string Check = "core.client-events";

        [Fact]
        public async Task The_core_events_and_a_features_own_pass()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync("job.changed", "assignments.changed", "acme.document-state.changed");

            Assert.Empty(problems.From(Check));
        }

        [Theory]
        [InlineData("changed")]
        [InlineData("Inbox.changed")]
        [InlineData("inbox.Changed")]
        [InlineData("inbox_changed")]
        [InlineData("inbox..changed")]
        [InlineData(".inbox.changed")]
        [InlineData("")]
        public async Task A_malformed_name_is_refused(string name)
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(name)).From(Check));

            Assert.Equal($"The client event '{name}' of the feature 'fixture' does not have a well-formed name.", problem.Problem);
        }

        [Fact]
        public async Task A_name_over_sixty_four_characters_is_refused()
        {
            string longest = "fixture." + new string('a', 56);

            Assert.Empty((await CheckAsync(longest)).From(Check));
            _ = Assert.Single((await CheckAsync(longest + "a")).From(Check));
        }

        [Fact]
        public async Task A_name_registered_twice_names_both_features()
        {
            CompositionProblem problem = Assert.Single((await CheckAsync("inbox.changed")).From(Check));

            Assert.Equal("The client event 'inbox.changed' is registered more than once, by the features 'core' and 'fixture'.", problem.Problem);
        }

        /// <summary>Runs every check over a composition in which one feature, <c>fixture</c>, registers the events.</summary>
        /// <param name="names">The events' names.</param>
        /// <returns>The problems.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(params string[] names)
        {
            return TestComposition.CheckAsync(tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution =>
            {
                foreach (string name in names)
                {
                    _ = contribution.ClientEvent(name);
                }
            })));
        }
    }
}
