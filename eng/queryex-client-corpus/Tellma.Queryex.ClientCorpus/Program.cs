// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Queryex.Testing.Client;

namespace Tellma.Queryex.ClientCorpus
{
    /// <summary>
    ///     Writes the Queryex lexer corpus and print golden into the client's entity sources, from
    ///     the engine's own lexer, parser and printer.
    /// </summary>
    internal static class Program
    {
        /// <summary>Generates both files and writes them.</summary>
        /// <param name="args">Optionally, the repository root; found from the working directory otherwise.</param>
        /// <returns>0 when both files were written; 1 when the repository root could not be found.</returns>
        internal static int Main(string[] args)
        {
            // --- The repository root: given, or the nearest folder holding the solution ---
            string? root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot(Directory.GetCurrentDirectory());
            if (root is null || !File.Exists(Path.Combine(root, "Tellma.slnx")))
            {
                Console.Error.WriteLine("Usage: Tellma.Queryex.ClientCorpus [<repository root>]");
                Console.Error.WriteLine("  Run from inside the repository, or name its root.");
                return 1;
            }

            // --- Both files, UTF-8 without a byte-order mark and with line feeds, on every OS ---
            string directory = Path.Combine(root, ClientCorpusFiles.ClientDirectory);
            UTF8Encoding encoding = new(encoderShouldEmitUTF8Identifier: false);
            string lexer = Path.Combine(directory, ClientCorpusFiles.LexerCorpusFileName);
            string print = Path.Combine(directory, ClientCorpusFiles.PrintGoldenFileName);
            File.WriteAllText(lexer, ClientCorpusFiles.LexerCorpus(), encoding);
            File.WriteAllText(print, ClientCorpusFiles.PrintGolden(), encoding);

            Console.WriteLine($"Wrote {ClientLexerCorpus.Texts.Count} lexer cases to {lexer}.");
            Console.WriteLine($"Wrote {ClientPrintGolden.Rows.Count} print rows to {print}.");
            return 0;
        }

        /// <summary>Finds the repository root above a folder.</summary>
        /// <param name="start">The folder to search from.</param>
        /// <returns>The nearest folder holding the solution file, or null when there is none.</returns>
        private static string? FindRoot(string start)
        {
            for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "Tellma.slnx")))
                {
                    return current.FullName;
                }
            }

            return null;
        }
    }
}
