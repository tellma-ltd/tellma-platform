// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Localization;

namespace Tellma.CultureData.Generator
{
    /// <summary>
    ///     Writes the pinned culture data of Tellma.Core from this host's ICU. Run it on the one
    ///     ICU release the platform pins; the project README gives the command.
    /// </summary>
    internal static class Program
    {
        /// <summary>Generates the data and writes it to a file.</summary>
        /// <param name="args">The output path, then a description of this host's CLDR and ICU releases.</param>
        /// <returns>0 when the file was written; 1 when the arguments are missing.</returns>
        internal static int Main(string[] args)
        {
            if (args.Length != 2 || string.IsNullOrWhiteSpace(args[0]) || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine("Usage: Tellma.CultureData.Generator <output path> <source description>");
                Console.Error.WriteLine("  e.g. src/core/Tellma.Core/Localization/CultureData.txt \"CLDR 48 (ICU 78.2), mcr.microsoft.com/dotnet/sdk:10.0-resolute\"");
                return 1;
            }

            // The description may not break the comment line it is written on.
            string source = args[1].ReplaceLineEndings(" ").Trim();
            string text = CultureDataSource.Generate(source);

            // UTF-8 without a byte-order mark and with line feeds, on every OS: the file is
            // embedded as it is and diffed as text.
            File.WriteAllText(args[0], text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Console.WriteLine($"Wrote {CultureDataSource.CataloguePairs().Count()} cultures to {args[0]}.");
            Console.WriteLine($"Host ICU fingerprint: {CultureDataFile.HostFingerprint()}.");
            return 0;
        }
    }
}
