// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Connector.MarminAe.Tests.Infrastructure
{
    /// <summary>Where a vector came from.</summary>
    internal enum VectorProvenance
    {
        /// <summary>Captured from the vendor's sandbox.</summary>
        Recorded = 0,

        /// <summary>Hand-authored, because no capture of this response exists.</summary>
        Synthetic = 1,
    }

    /// <summary>One parser input, and where it came from.</summary>
    /// <param name="LogicalName">The name tests ask for it by.</param>
    /// <param name="FileName">The file it was read from, provenance suffix included.</param>
    /// <param name="Content">The body.</param>
    /// <param name="Provenance">Whether it was captured or authored.</param>
    internal sealed record Vector(
        string LogicalName, string FileName, string Content, VectorProvenance Provenance);

    /// <summary>Loads the parser inputs the response and error suites read.</summary>
    /// <remarks>
    ///     Vectors resolve by logical name, a recording winning over a stand-in of the same name, so
    ///     replacing one with a capture is dropping in a file and deleting its predecessor. The
    ///     provenance suffix in the file name is the disclosure: JSON cannot carry a comment, and the
    ///     file name is the one marker that survives being read in a diff, a stack trace, or the
    ///     build output. See Vectors/PROVENANCE.md — and the offline suite README — for what a
    ///     synthetic vector is, and is not, evidence of.
    /// </remarks>
    internal static class Vectors
    {
        private const string RecordedMarker = ".recorded.";
        private const string SyntheticMarker = ".synthetic.";

        /// <summary>The folder the vectors are copied into next to the test assembly.</summary>
        internal static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "Vectors");

        /// <summary>Reads a vector.</summary>
        /// <param name="logicalName">Its folder and name, without the provenance suffix or the
        ///     extension — for example <c>Errors/400-field-validation</c>.</param>
        /// <returns>The vector.</returns>
        internal static Vector Load(string logicalName)
        {
            return LoadFrom(Root, logicalName);
        }

        /// <summary>Reads a vector from a folder other than the shipped one.</summary>
        /// <remarks>
        ///     For the provenance suite, which needs somewhere of its own to write: the folder the
        ///     vectors ship in is enumerated by another test, and a file left behind by a killed run
        ///     would be counted as a recording.
        /// </remarks>
        /// <param name="root">The folder to resolve against.</param>
        /// <param name="logicalName">The vector's folder and name.</param>
        /// <returns>The vector.</returns>
        internal static Vector LoadFrom(string root, string logicalName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);

            string relative = logicalName.Replace('/', Path.DirectorySeparatorChar);
            string directory = Path.GetDirectoryName(Path.Combine(root, relative))
                ?? throw new InvalidOperationException($"'{logicalName}' names no folder.");
            string name = Path.GetFileName(relative);

            string[] recorded = Directory.Exists(directory)
                ? Directory.GetFiles(directory, name + RecordedMarker + "*")
                : [];
            string[] synthetic = Directory.Exists(directory)
                ? Directory.GetFiles(directory, name + SyntheticMarker + "*")
                : [];

            string[] candidates = recorded.Length > 0 ? recorded : synthetic;

            if (candidates.Length != 1)
            {
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{logicalName}' resolved to {candidates.Length} files under {directory}; it must resolve to exactly one."));
            }

            string path = candidates[0];

            return new Vector(
                logicalName,
                Path.GetFileName(path),
                File.ReadAllText(path),
                recorded.Length > 0 ? VectorProvenance.Recorded : VectorProvenance.Synthetic);
        }

        /// <summary>Every vector file that shipped with the suite.</summary>
        /// <returns>The full paths, in no particular order.</returns>
        internal static IReadOnlyList<string> AllFiles()
        {
            return Directory.Exists(Root)
                ? Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                : [];
        }

        /// <summary>Whether a file name declares where its contents came from.</summary>
        /// <param name="fileName">The file name.</param>
        /// <returns>True when it carries a provenance marker.</returns>
        internal static bool DeclaresProvenance(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);

            return fileName.Contains(RecordedMarker, StringComparison.Ordinal)
                || fileName.Contains(SyntheticMarker, StringComparison.Ordinal);
        }
    }
}
