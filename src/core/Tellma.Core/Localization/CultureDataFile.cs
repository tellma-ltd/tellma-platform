// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The pinned culture data: one <see cref="CultureSnapshot" /> per language and region of the
    ///     language catalogue, generated once from one ICU release and checked in, so server text
    ///     reads the same on every host.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The text form is line-oriented. A line starting with <c>#</c> is a comment; two of
    ///         them carry the data's origin (<c># source:</c>, a description, and <c># host:</c>,
    ///         the fingerprint of the ICU data it was read from). A <c>[language]</c> section holds
    ///         every field as <c>name=value</c>; a <c>[language-REGION]</c> section after it holds
    ///         only the fields in which that pair differs from its language's section.
    ///     </para>
    ///     <para>
    ///         The data is embedded in the assembly. It is regenerated, never edited by hand.
    ///     </para>
    /// </remarks>
    internal sealed class CultureDataFile
    {
        /// <summary>The name of the embedded resource holding the pinned data.</summary>
        public const string ResourceName = "Tellma.Core.Localization.CultureData.txt";

        private const string SourcePrefix = "# source: ";
        private const string HostPrefix = "# host: ";

        private static readonly Lazy<CultureDataFile> Embedded = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Creates the data.</summary>
        /// <param name="source">What the data was generated from, for a reader.</param>
        /// <param name="host">The fingerprint of the ICU data it was read from.</param>
        /// <param name="pairs">The pairs, in the data's order.</param>
        /// <param name="cultures">Each pair's snapshot.</param>
        private CultureDataFile(string source, string host, IReadOnlyList<string> pairs, Dictionary<string, CultureSnapshot> cultures)
        {
            Source = source;
            Host = host;
            Pairs = pairs;
            Cultures = cultures.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The data embedded in this assembly.</summary>
        public static CultureDataFile Pinned => Embedded.Value;

        /// <summary>What the data was generated from, for a reader: the CLDR and ICU releases and the image.</summary>
        public string Source { get; }

        /// <summary>The fingerprint of the ICU data the values were read from (<see cref="HostFingerprint" />).</summary>
        public string Host { get; }

        /// <summary>The pairs (<c>ar-SA</c>), in the data's order.</summary>
        public IReadOnlyList<string> Pairs { get; }

        /// <summary>Each pair's snapshot, by pair, without regard to case.</summary>
        public FrozenDictionary<string, CultureSnapshot> Cultures { get; }

        /// <summary>
        ///     The fingerprint of this host's ICU data: its collation version, which every ICU
        ///     release changes. Two hosts with one fingerprint read the same culture data.
        /// </summary>
        /// <returns>The fingerprint, as eight hexadecimal digits.</returns>
        public static string HostFingerprint()
        {
            return CultureInfo.GetCultureInfo("en-US").CompareInfo.Version.FullVersion.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>Reads the text form.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The data.</returns>
        /// <exception cref="FormatException">The text is not well formed; the message names the line.</exception>
        public static CultureDataFile Parse(string text)
        {
            string source = string.Empty;
            string host = string.Empty;
            List<string> pairs = [];
            Dictionary<string, CultureSnapshot> cultures = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Dictionary<string, string>> languages = new(StringComparer.Ordinal);

            string? section = null;
            Dictionary<string, string> values = [];
            int number = 0;

            // Closes the section read so far: a language's fields are kept for its pairs, and a
            // pair's fields are laid over its language's.
            void Close()
            {
                if (section is null)
                {
                    return;
                }

                int hyphen = section.IndexOf('-', StringComparison.Ordinal);
                if (hyphen < 0)
                {
                    languages[section] = values;
                }
                else
                {
                    if (!languages.TryGetValue(section[..hyphen], out Dictionary<string, string>? defaults))
                    {
                        throw new FormatException($"Line {number}: the section [{section}] comes before its language's section [{section[..hyphen]}].");
                    }

                    Dictionary<string, string> merged = new(defaults, StringComparer.Ordinal);
                    foreach ((string name, string value) in values)
                    {
                        merged[name] = value;
                    }

                    try
                    {
                        if (!cultures.TryAdd(section, new CultureSnapshot(merged)))
                        {
                            throw new FormatException($"Line {number}: the section [{section}] appears twice.");
                        }
                    }
                    catch (ArgumentException exception)
                    {
                        throw new FormatException($"The section [{section}] ending at line {number} is incomplete: {exception.Message}", exception);
                    }

                    pairs.Add(section);
                }

                values = [];
            }

            foreach (string raw in text.Split('\n'))
            {
                number++;
                string line = raw.TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }

                // --- Comments, two of which carry the origin ---
                if (line[0] == '#')
                {
                    if (line.StartsWith(SourcePrefix, StringComparison.Ordinal))
                    {
                        source = line[SourcePrefix.Length..];
                    }
                    else if (line.StartsWith(HostPrefix, StringComparison.Ordinal))
                    {
                        host = line[HostPrefix.Length..];
                    }

                    continue;
                }

                // --- A section ---
                if (line[0] == '[')
                {
                    if (line[^1] != ']' || line.Length < 3)
                    {
                        throw new FormatException($"Line {number}: '{line}' is not a section such as [ar] or [ar-SA].");
                    }

                    Close();
                    section = line[1..^1];
                    continue;
                }

                // --- A field ---
                int equals = line.IndexOf('=', StringComparison.Ordinal);
                if (equals <= 0 || section is null)
                {
                    throw new FormatException($"Line {number}: '{line}' is not a name=value field inside a section.");
                }

                if (!values.TryAdd(line[..equals], line[(equals + 1)..]))
                {
                    throw new FormatException($"Line {number}: the field '{line[..equals]}' appears twice in [{section}].");
                }
            }

            Close();
            return new CultureDataFile(source, host, pairs.AsReadOnly(), cultures);
        }

        /// <summary>Writes the text form.</summary>
        /// <param name="source">What the data was generated from, for a reader.</param>
        /// <param name="host">The fingerprint of the ICU data it was read from.</param>
        /// <param name="cultures">Each pair with its snapshot; the pairs of one language are adjacent.</param>
        /// <returns>The text, with line-feed line endings.</returns>
        public static string Write(string source, string host, IReadOnlyList<KeyValuePair<string, CultureSnapshot>> cultures)
        {
            StringBuilder builder = new();
            _ = builder
                .Append("# The culture data of every language and region of the Tellma language catalogue.\n")
                .Append("# Generated by eng/culture-data; regenerate it, never edit it by hand.\n")
                .Append(SourcePrefix).Append(source).Append('\n')
                .Append(HostPrefix).Append(host).Append('\n');

            foreach (IGrouping<string, KeyValuePair<string, CultureSnapshot>> language in cultures
                .GroupBy(static pair => pair.Key[..pair.Key.IndexOf('-', StringComparison.Ordinal)], StringComparer.Ordinal))
            {
                // --- The language's section: each field's most common value among its pairs ---
                // The first pair to carry a most common value decides a tie, so the text is stable.
                Dictionary<string, string> defaults = new(StringComparer.Ordinal);
                _ = builder.Append("\n[").Append(language.Key).Append("]\n");
                foreach (CultureField field in CultureField.All)
                {
                    string common = language
                        .GroupBy(pair => pair.Value[field.Name], StringComparer.Ordinal)
                        .MaxBy(static group => group.Count())!
                        .Key;
                    defaults.Add(field.Name, common);
                    _ = builder.Append(field.Name).Append('=').Append(common).Append('\n');
                }

                // --- Each pair's section: only what differs ---
                foreach ((string pair, CultureSnapshot snapshot) in language)
                {
                    _ = builder.Append("\n[").Append(pair).Append("]\n");
                    foreach (CultureField field in CultureField.All)
                    {
                        if (!string.Equals(snapshot[field.Name], defaults[field.Name], StringComparison.Ordinal))
                        {
                            _ = builder.Append(field.Name).Append('=').Append(snapshot[field.Name]).Append('\n');
                        }
                    }
                }
            }

            return builder.ToString();
        }

        /// <summary>Reads the data embedded in this assembly.</summary>
        /// <returns>The data.</returns>
        /// <exception cref="InvalidOperationException">The assembly was built without the data.</exception>
        private static CultureDataFile LoadEmbedded()
        {
            using Stream stream = typeof(CultureDataFile).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"The assembly does not embed the culture data '{ResourceName}'.");
            using StreamReader reader = new(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd());
        }
    }
}
