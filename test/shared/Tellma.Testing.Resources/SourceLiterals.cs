// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Testing.Resources
{
    /// <summary>
    ///     The string literals of a source tree: every C# (<c>.cs</c>) and TypeScript (<c>.ts</c>)
    ///     file under the given roots, read with a tokenizer that skips comments, so a key spelled in
    ///     a comment is not a use of it.
    /// </summary>
    /// <remarks>
    ///     Build output, dependencies and tool state are skipped (<c>bin</c>, <c>obj</c>,
    ///     <c>node_modules</c>, <c>dist</c>, <c>out-tsc</c>, <c>coverage</c> and every directory
    ///     whose name starts with a dot), and so are TypeScript tests (<c>*.spec.ts</c>), since a key
    ///     only a test spells is used by nothing that ships.
    /// </remarks>
    public static class SourceLiterals
    {
        /// <summary>The directories that hold no source of the tree's own.</summary>
        private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", "dist", "out-tsc", "coverage",
        };

        /// <summary>Every literal of the C# and TypeScript files under the roots.</summary>
        /// <param name="roots">The directories to read; a root that does not exist is an error, so a moved tree never audits nothing.</param>
        /// <returns>The literals' contents, compared ordinally.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="roots" /> is null.</exception>
        /// <exception cref="DirectoryNotFoundException">A root does not exist.</exception>
        public static IReadOnlySet<string> Read(IEnumerable<string> roots)
        {
            ArgumentNullException.ThrowIfNull(roots);

            HashSet<string> literals = new(StringComparer.Ordinal);
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    throw new DirectoryNotFoundException($"The source root '{root}' does not exist.");
                }

                foreach (string file in Files(root))
                {
                    string text = File.ReadAllText(file).ReplaceLineEndings("\n");
                    literals.UnionWith(file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? InCSharp(text) : InTypeScript(text));
                }
            }

            return literals;
        }

        /// <summary>The literals of C# source, in source order.</summary>
        /// <param name="text">The source text.</param>
        /// <returns>Each literal's content; each text run of an interpolated string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="text" /> is null.</exception>
        public static IReadOnlyList<string> InCSharp(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return CSharpLiteralScanner.Scan(text);
        }

        /// <summary>The literals of TypeScript source, in source order.</summary>
        /// <param name="text">The source text.</param>
        /// <returns>Each literal's content; each text run of a template literal.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="text" /> is null.</exception>
        public static IReadOnlyList<string> InTypeScript(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return TypeScriptLiteralScanner.Scan(text);
        }

        /// <summary>The source files under a directory, skipping the directories that hold none of the tree's own.</summary>
        /// <param name="directory">The directory.</param>
        /// <returns>The files.</returns>
        private static IEnumerable<string> Files(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || (name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return file;
                }
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (name.StartsWith('.') || SkippedDirectories.Contains(name))
                {
                    continue;
                }

                foreach (string file in Files(child))
                {
                    yield return file;
                }
            }
        }
    }
}
