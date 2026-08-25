// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.Tests.Coverage
{
    /// <summary>
    ///     Checks that the second reading of the language covers all of it.
    /// </summary>
    /// <remarks>
    ///     A second implementation is only evidence for the parts it actually implements. Comparing
    ///     the two readings function by function is what stops one of them quietly falling behind
    ///     the other as the library grows.
    /// </remarks>
    public sealed class ReferenceCoverageTests
    {
        /// <summary>Every function the language has is one the second reading covers.</summary>
        [Fact]
        public void EveryFunction_IsCovered()
        {
            Assert.Empty(ReferenceSemantics.UncoveredFunctions());
        }

        /// <summary>The second reading covers nothing the language does not have.</summary>
        /// <remarks>
        ///     A reading of a function that was renamed or removed would otherwise sit there
        ///     unreachable, and look like coverage.
        /// </remarks>
        [Fact]
        public void NothingExtra_IsCovered()
        {
            Assert.Empty(ReferenceSemantics.UnknownFunctions());
        }

        /// <summary>
        ///     Every function the language has is one a corpus case actually evaluates.
        /// </summary>
        /// <remarks>
        ///     What makes the two checks above mean anything. They compare the registry against a
        ///     list the second reading keeps, and a name added to that list without an arm in the
        ///     evaluator satisfies both while evaluating nothing. This runs the evaluator over the
        ///     corpus, so a function is only covered once some case has actually reached it.
        /// </remarks>
        [Fact]
        public void EveryFunction_IsExercised()
        {
            Assert.Empty(ReferenceSemantics.UnexercisedFunctions());
        }

        /// <summary>
        ///     Every piece of text the fixture holds is one the collation table has weights for.
        /// </summary>
        /// <remarks>
        ///     The table covers a small repertoire on purpose. This is what makes stepping outside
        ///     it a decision somebody has to make, with the weights to go with it, rather than
        ///     something that happens the first time an unusual character is typed into a fixture.
        /// </remarks>
        [Fact]
        public void EveryFixtureValue_IsInTheRepertoire()
        {
            string[] outside = [.. LedgerData.AllText().Where(text => !Collation.IsInRepertoire(text))];

            Assert.Empty(outside);
        }

        /// <summary>Every expression the corpus holds stays inside the repertoire too.</summary>
        [Fact]
        public void EveryCorpusExpression_IsInTheRepertoire()
        {
            string[] outside = [.. ExpressionCorpus.All
                .Select(entry => entry.Text)
                .Where(text => !Collation.IsInRepertoire(text))];

            Assert.Empty(outside);
        }
    }
}
