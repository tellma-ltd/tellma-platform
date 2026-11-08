// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Corpus;

namespace Tellma.Core.Queryex.Tests.Coverage
{
    /// <summary>
    ///     Checks that every form a diagnostic's location can take inside a filter tree is one some
    ///     corpus case pins, code, span and arguments included.
    /// </summary>
    /// <remarks>
    ///     A host parses locations to attribute a problem to the criterion that caused it. A form no
    ///     case has ever pinned is a form nobody has checked the spelling, the nesting or the span of.
    /// </remarks>
    public sealed class LocationCoverageTests
    {
        /// <summary>
        ///     The location components a filter tree adds, each with a problem that has to be reported
        ///     inside one: a binding problem, and the ceilings lowering counts.
        /// </summary>
        public static TheoryData<string, string> Forms => new()
        {
            { ".Via[", "QX3001" },
            { ".Via[", "QX5006" },
            { ".Exists[", "QX3001" },
            { ".Exists[", "QX5006" },
            { ".Exists[", "QX5007" },
            { ".And[", "QX5006" },
            { ".Or[", "QX5007" },
        };

        /// <summary>Each form is pinned by a corpus case reporting that problem inside it.</summary>
        /// <param name="component">The location component.</param>
        /// <param name="code">The problem.</param>
        [Theory]
        [MemberData(nameof(Forms))]
        public void EachForm_IsPinnedByACorpusCase(string component, string code)
        {
            Assert.Contains(
                QueryCorpus.All.SelectMany(entry => entry.Sites),
                site => site.Code == code
                    && site.Location is not null
                    && site.Location.Contains(component, StringComparison.Ordinal));
        }

        /// <summary>A pinned diagnostic's code is one its case lists among the codes it expects.</summary>
        [Fact]
        public void EveryPinnedCode_IsAnExpectedCode()
        {
            foreach (QueryCase entry in QueryCorpus.All)
            {
                foreach (DiagnosticSite site in entry.Sites)
                {
                    Assert.Contains(site.Code, entry.Diagnostics);
                }
            }
        }
    }
}
