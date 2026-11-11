// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     The two strings <c>CreateProcess</c> takes, built in managed code that runs, and is
    ///     tested, on every platform.
    /// </summary>
    internal static class Win32Text
    {
        /// <summary>
        ///     Builds a command line that the C runtime splits back into exactly these arguments:
        ///     each is quoted, and backslashes before a quote are doubled.
        /// </summary>
        /// <param name="application">The executable, as the first argument.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The command line.</returns>
        internal static string CommandLine(string application, IReadOnlyList<string> arguments)
        {
            var line = new StringBuilder();
            foreach (string argument in (IEnumerable<string>)[application, .. arguments])
            {
                if (line.Length > 0)
                {
                    _ = line.Append(' ');
                }

                _ = line.Append('"');
                int backslashes = 0;
                foreach (char c in argument)
                {
                    if (c == '\\')
                    {
                        backslashes++;
                        continue;
                    }

                    // Backslashes before a quote escape it; the quote itself is escaped too.
                    _ = line.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
                    backslashes = 0;
                }

                // Backslashes before the closing quote are doubled, so they do not escape it.
                _ = line.Append('\\', backslashes * 2).Append('"');
            }

            return line.ToString();
        }

        /// <summary>Builds a Unicode environment block: sorted <c>name=value</c> strings, each ended by a NUL, then a NUL.</summary>
        /// <param name="environment">The variables.</param>
        /// <returns>The block.</returns>
        internal static string EnvironmentBlock(IReadOnlyDictionary<string, string> environment)
        {
            var block = new StringBuilder();
            foreach (KeyValuePair<string, string> variable in environment.OrderBy(static v => v.Key, StringComparer.OrdinalIgnoreCase))
            {
                _ = block.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');
            }

            return block.Append('\0').ToString();
        }
    }
}
