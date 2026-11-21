// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.entity-shapes</c>: the registered leaf carries every shape required of its entity.</summary>
    public class EntityShapesCheckTests
    {
        private const string Check = "core.entity-shapes";

        [Fact]
        public async Task A_default_leaf_without_the_shape_is_reported_with_a_leaf_to_derive_and_the_members_to_add()
        {
            List<CompositionProblem> problems = await CheckAsync(static _ => { });

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("The feature 'compliance' requires the entity", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Derive a leaf from SampleEntity that implements IEInvoiceTracked", problem.Fix, StringComparison.Ordinal);
            Assert.Contains("UseEntity<SampleEntity, TLeaf>()", problem.Fix, StringComparison.Ordinal);

            // The members to write, and not the default-implemented one.
            Assert.Contains("string? EInvoiceStatus { get; set; }", problem.Fix, StringComparison.Ordinal);
            Assert.Contains("Guid? EInvoiceUuid { get; init; }", problem.Fix, StringComparison.Ordinal);
            Assert.DoesNotContain("IsAccepted", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_substituted_leaf_without_the_shape_is_told_to_implement_it()
        {
            List<CompositionProblem> problems = await CheckAsync(static tellma => tellma.UseEntity<SampleEntity, SampleLeaf>());

            Assert.StartsWith("Implement IEInvoiceTracked on SampleLeaf.", Assert.Single(problems).Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_substituted_leaf_with_the_shape_passes()
        {
            Assert.Empty(await CheckAsync(static tellma => tellma.UseEntity<SampleEntity, TrackedLeaf>()));
        }

        private static async Task<List<CompositionProblem>> CheckAsync(Action<TellmaBuilder> substitute)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma =>
            {
                tellma.AddFeature(new TestFeature<Tag.A>("compliance", contribute: static c => c.RequiresShape<SampleEntity, IEInvoiceTracked>()));
                substitute(tellma);
            });

            return problems.From(Check);
        }
    }
}
