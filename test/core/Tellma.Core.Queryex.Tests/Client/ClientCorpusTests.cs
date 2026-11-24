// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Client;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Client
{
    /// <summary>
    ///     The lexer corpus and print golden the client's Queryex suites read: current with the
    ///     engine, and every print row a text the engine reads back as itself.
    /// </summary>
    public sealed class ClientCorpusTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The names of the print rows.</summary>
        public static TheoryData<string> PrintRows { get; } = [.. ClientPrintGolden.Rows.Select(static row => row.Name)];

        /// <summary>The checked-in lexer corpus is what the engine's lexer writes now.</summary>
        [Fact]
        public void TheLexerCorpus_IsCurrent()
        {
            Assert.Equal(ClientCorpusFiles.LexerCorpus(), CheckedIn(ClientCorpusFiles.LexerCorpusFileName));
        }

        /// <summary>The checked-in print golden is what the engine's printer writes now.</summary>
        [Fact]
        public void ThePrintGolden_IsCurrent()
        {
            Assert.Equal(ClientCorpusFiles.PrintGolden(), CheckedIn(ClientCorpusFiles.PrintGoldenFileName));
        }

        /// <summary>
        ///     A row's text parses cleanly, prints as itself, and means what its grouped source means.
        /// </summary>
        /// <param name="name">The row's name.</param>
        [Theory]
        [MemberData(nameof(PrintRows))]
        public void APrintRow_ReadsBackAsItself(string name)
        {
            ClientPrintRow row = ClientPrintGolden.Rows.Single(candidate => candidate.Name == name);
            string text = ClientPrintGolden.Render(row).Text;

            ProbeSyntax parsed = QueryexProbe.Parse(text);
            Assert.True(parsed.Succeeded, string.Join(", ", parsed.Diagnostics.Select(static diagnostic => diagnostic.Code)));
            Assert.Equal(text, parsed.CanonicalText);
            Assert.True(QueryexProbe.AreEquivalent(row.Source, text));
        }

        /// <summary>A filter row reads cleanly as a filter on its fixture entity.</summary>
        /// <param name="name">The row's name.</param>
        [Theory]
        [MemberData(nameof(PrintRows))]
        public void AFilterRow_DiscoversCleanly(string name)
        {
            ClientPrintRow row = ClientPrintGolden.Rows.Single(candidate => candidate.Name == name);
            if (row.Root is null)
            {
                // A row about syntax alone names paths and functions no schema holds.
                return;
            }

            DiscoveryResult result = Engine.Discover(ClientPrintGolden.Render(row).Text, new DiscoveryOptions
            {
                Schema = LedgerFixture.Schema,
                Root = LedgerFixture.Schema.FindEntity(row.Root),
                Mode = QueryexMode.Filter,
            });

            Assert.Empty(result.Diagnostics);
        }

        /// <summary>
        ///     Every clean text's parameters, grouped as the corpus groups them, are the parameters
        ///     discovery reports, at the same sites.
        /// </summary>
        [Fact]
        public void TheCorpusParameters_AreTheOnesDiscoveryReports()
        {
            int checkedTexts = 0;
            foreach (string text in ClientLexerCorpus.Texts)
            {
                ClientLexerCase lexed = ClientLexerCorpus.Lex(text);
                if (lexed.Diagnostics.Count > 0 || !QueryexProbe.Parse(text).Succeeded)
                {
                    continue;
                }

                DiscoveryResult result = Engine.Discover(text, new DiscoveryOptions());
                Assert.Equal(
                    lexed.Parameters.Select(static parameter => Describe(parameter.Name, parameter.Sites.Select(static site => (site.Start, site.Length)))),
                    result.Parameters.Select(static use => Describe(use.Name, use.Occurrences.Select(static site => (site.Span.Start, site.Span.Length)))));
                checkedTexts += lexed.Parameters.Count > 0 ? 1 : 0;
            }

            // The comparison must have had parameters to compare.
            Assert.True(checkedTexts >= 3);
        }

        /// <summary>Reads one checked-in client file, with its line endings as the repository stores them.</summary>
        /// <param name="file">The file name.</param>
        /// <returns>The file's text.</returns>
        private static string CheckedIn(string file)
        {
            // A checkout that converted line endings is still current.
            string path = Path.Combine(AppContext.BaseDirectory, "Client", file);
            return File.ReadAllText(path).ReplaceLineEndings("\n");
        }

        /// <summary>Describes one parameter group, its name's case aside.</summary>
        /// <param name="name">The group's name.</param>
        /// <param name="sites">Its sites, in order.</param>
        /// <returns>The description.</returns>
        private static string Describe(string name, IEnumerable<(int Start, int Length)> sites)
        {
            return name.ToUpperInvariant() + ": " + string.Join(", ", sites.Select(static site => $"{site.Start}+{site.Length}"));
        }
    }
}
