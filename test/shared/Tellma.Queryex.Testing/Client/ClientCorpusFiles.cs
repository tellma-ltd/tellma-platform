// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Queryex.Testing.Client
{
    /// <summary>
    ///     The two files the client's Queryex suites read: the lexer corpus and the print golden,
    ///     as the engine writes them.
    /// </summary>
    /// <remarks>
    ///     One writer serves both the generator that checks the files in and the engine test that
    ///     fails when they are stale, so the two can never disagree about the format.
    /// </remarks>
    public static class ClientCorpusFiles
    {
        /// <summary>The file name of the lexer corpus.</summary>
        public const string LexerCorpusFileName = "queryex-lexer-corpus.json";

        /// <summary>The file name of the print golden.</summary>
        public const string PrintGoldenFileName = "queryex-print-golden.json";

        /// <summary>Where both files live, relative to the repository root.</summary>
        public const string ClientDirectory = "client/projects/core/tellma-core/entity/src";

        /// <summary>The command that regenerates both files, run from the repository root.</summary>
        public const string RegenerateCommand = "dotnet run --project eng/queryex-client-corpus/Tellma.Queryex.ClientCorpus";

        /// <summary>Writes the lexer corpus.</summary>
        /// <returns>The file's text: UTF-8 content with line feeds, ending in one.</returns>
        public static string LexerCorpus()
        {
            StringBuilder builder = new();
            builder.Append("{\n");
            Member(builder, 1, "description", "What the engine's lexer makes of each text: its tokens, with the runs it could not read as error tokens; its parameters, grouped by name; and its problems. The client's lexer must agree with every case.", last: false);
            Member(builder, 1, "regenerate", RegenerateCommand, last: false);
            builder.Append("  \"cases\": [\n");

            IReadOnlyList<string> texts = ClientLexerCorpus.Texts;
            for (int index = 0; index < texts.Count; index++)
            {
                ClientLexerCase lexed = ClientLexerCorpus.Lex(texts[index]);

                builder.Append("    {\n");
                Member(builder, 3, "text", lexed.Text, last: false);
                List(builder, 3, "tokens", lexed.Tokens.Select(static token => ClientJson.Object(
                    ("kind", token.Kind),
                    ("start", token.Start),
                    ("length", token.Length),
                    ("text", token.Text),
                    ("call", token.Call is true ? ClientJson.Boolean(true) : null))), last: false);
                List(builder, 3, "parameters", lexed.Parameters.Select(static parameter => ClientJson.Object(
                    ("name", parameter.Name),
                    ("sites", ClientJson.Array(parameter.Sites.Select(static site => ClientJson.Object(
                        ("start", site.Start),
                        ("length", site.Length))))))), last: false);
                List(builder, 3, "diagnostics", lexed.Diagnostics.Select(static diagnostic => ClientJson.Object(
                    ("code", diagnostic.Code),
                    ("start", diagnostic.Start),
                    ("length", diagnostic.Length))), last: true);
                builder.Append(index == texts.Count - 1 ? "    }\n" : "    },\n");
            }

            builder.Append("  ]\n}\n");
            return builder.ToString();
        }

        /// <summary>Writes the print golden.</summary>
        /// <returns>The file's text: UTF-8 content with line feeds, ending in one.</returns>
        public static string PrintGolden()
        {
            StringBuilder builder = new();
            builder.Append("{\n");
            Member(builder, 1, "description", "Expression trees and the text the engine's canonical printer writes for each. The client's printer must print every tree as its text.", last: false);
            Member(builder, 1, "regenerate", RegenerateCommand, last: false);
            builder.Append("  \"rows\": [\n");

            IReadOnlyList<ClientPrintRow> rows = ClientPrintGolden.Rows;
            for (int index = 0; index < rows.Count; index++)
            {
                ClientPrintCase printed = ClientPrintGolden.Render(rows[index]);

                builder.Append("    {\n");
                Member(builder, 3, "name", printed.Name, last: false);
                Member(builder, 3, "text", printed.Text, last: false);
                builder.Append("      \"expression\": ").Append(printed.Expression).Append('\n');
                builder.Append(index == rows.Count - 1 ? "    }\n" : "    },\n");
            }

            builder.Append("  ]\n}\n");
            return builder.ToString();
        }

        /// <summary>Writes one string member on its own line.</summary>
        /// <param name="builder">Where to write.</param>
        /// <param name="depth">The nesting depth, two spaces each.</param>
        /// <param name="name">The member's name.</param>
        /// <param name="value">The member's value.</param>
        /// <param name="last">Whether it is the object's last member.</param>
        private static void Member(StringBuilder builder, int depth, string name, string value, bool last)
        {
            builder.Append(' ', depth * 2)
                .Append(ClientJson.String(name))
                .Append(": ")
                .Append(ClientJson.String(value))
                .Append(last ? "\n" : ",\n");
        }

        /// <summary>Writes an array member, one item per line, so a change reads as a line diff.</summary>
        /// <param name="builder">Where to write.</param>
        /// <param name="depth">The nesting depth, two spaces each.</param>
        /// <param name="name">The member's name.</param>
        /// <param name="items">The items, in order.</param>
        /// <param name="last">Whether it is the object's last member.</param>
        private static void List(StringBuilder builder, int depth, string name, IEnumerable<ClientJson> items, bool last)
        {
            string indent = new(' ', depth * 2);
            List<ClientJson> written = [.. items];
            builder.Append(indent).Append(ClientJson.String(name)).Append(": [");
            if (written.Count == 0)
            {
                builder.Append(last ? "]\n" : "],\n");
                return;
            }

            builder.Append('\n');
            for (int index = 0; index < written.Count; index++)
            {
                builder.Append(indent).Append("  ").Append(written[index]).Append(index == written.Count - 1 ? "\n" : ",\n");
            }

            builder.Append(indent).Append(last ? "]\n" : "],\n");
        }
    }
}
