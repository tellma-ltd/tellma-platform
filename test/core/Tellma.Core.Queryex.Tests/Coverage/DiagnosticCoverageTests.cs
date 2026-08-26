// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Probe;

namespace Tellma.Core.Queryex.Tests.Coverage
{
    /// <summary>
    ///     Checks that every problem the engine can report is a problem some test provokes.
    /// </summary>
    /// <remarks>
    ///     A diagnostic nobody has ever seen produced is a diagnostic nobody has checked the span,
    ///     the arguments, or even the reachability of. Enumerating the catalogue and subtracting
    ///     what the corpus asserts turns "we should test that" into a build failure.
    /// </remarks>
    public sealed class DiagnosticCoverageTests
    {
        /// <summary>
        ///     The codes a dedicated test provokes rather than a corpus case, and which one.
        /// </summary>
        private static readonly FrozenDictionary<string, string> Elsewhere = new Dictionary<string, string>
        {
            ["QX3400"] = nameof(Binding.DiscoveryTests.Parameter_UsedAtTwoTypes_ReportsBoth),
        }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>
        ///     The codes nothing provokes, with the reason each is nonetheless declared.
        /// </summary>
        private static readonly FrozenDictionary<string, string> Unreachable = new Dictionary<string, string>
        {
            // Two overloads can only tie if the registry declares a pair that scores equally, and a
            // dedicated test proves no such pair exists. The code stays because the check that keeps
            // it unreachable is a property of the registry, not of the binder.
            ["QX3006"] = "no two registered overloads can score equally",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>Every declared code is provoked somewhere, or accounted for.</summary>
        [Fact]
        public void EveryCode_IsProvokedOrAccountedFor()
        {
            HashSet<string> unprovoked = [.. QueryexProbe.DiagnosticCodes];
            unprovoked.ExceptWith(Provoked());
            unprovoked.ExceptWith(Elsewhere.Keys);
            unprovoked.ExceptWith(Unreachable.Keys);

            Assert.Empty(unprovoked);
        }

        /// <summary>Nothing is excused from coverage that the corpus already covers.</summary>
        /// <remarks>
        ///     The list of excuses is the part that rots. Failing when an excuse stops being needed
        ///     is what keeps it from quietly outliving its reason.
        /// </remarks>
        [Fact]
        public void NoExcuse_IsStale()
        {
            HashSet<string> provoked = [.. Provoked()];
            Assert.Empty(Elsewhere.Keys.Where(provoked.Contains));
            Assert.Empty(Unreachable.Keys.Where(provoked.Contains));
        }

        /// <summary>Every code a corpus case asserts is a code the engine declares.</summary>
        [Fact]
        public void EveryAssertedCode_IsDeclared()
        {
            HashSet<string> undeclared = [.. Provoked()];
            undeclared.ExceptWith(QueryexProbe.DiagnosticCodes);

            Assert.Empty(undeclared);
        }

        /// <summary>Every code any corpus case asserts.</summary>
        /// <returns>The codes.</returns>
        private static IEnumerable<string> Provoked()
        {
            foreach (ExpressionCase entry in ExpressionCorpus.All)
            {
                foreach (string code in entry.Diagnostics)
                {
                    yield return code;
                }
            }

            foreach (QueryCase entry in QueryCorpus.All)
            {
                foreach (string code in entry.Diagnostics)
                {
                    yield return code;
                }
            }
        }
    }
}
