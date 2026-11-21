// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Xml.Linq;

namespace Tellma.Testing.Resources
{
    /// <summary>Reads the string entries of a <c>.resx</c> file, and finds a neutral file's satellites beside it.</summary>
    public static class ResxFile
    {
        /// <summary>The extension of a resource file.</summary>
        private const string Extension = ".resx";

        /// <summary>The string entries of a resource file, by key.</summary>
        /// <param name="path">The file.</param>
        /// <returns>Each string entry's value by its key, compared ordinally.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path" /> is null.</exception>
        /// <exception cref="InvalidOperationException">A key is declared twice.</exception>
        public static IReadOnlyDictionary<string, string> Read(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            // A data element with a type or a MIME type holds a file or a serialized object, never text.
            var document = XDocument.Load(path);
            Dictionary<string, string> entries = new(StringComparer.Ordinal);
            foreach (XElement data in document.Root?.Elements("data") ?? [])
            {
                if ((string?)data.Attribute("name") is not { } name || data.Attribute("type") is not null || data.Attribute("mimetype") is not null)
                {
                    continue;
                }

                if (!entries.TryAdd(name, (string?)data.Element("value") ?? string.Empty))
                {
                    throw new InvalidOperationException($"{path} declares the key '{name}' twice.");
                }
            }

            return entries;
        }

        /// <summary>The satellites of a neutral resource file: the <c>&lt;Name&gt;.&lt;culture&gt;.resx</c> files beside it.</summary>
        /// <param name="neutralPath">The neutral file, such as <c>Resources/Strings.resx</c>.</param>
        /// <returns>Each satellite's culture name and path, in ordinal order of the culture.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="neutralPath" /> is null.</exception>
        public static IReadOnlyList<(string Culture, string Path)> Satellites(string neutralPath)
        {
            ArgumentNullException.ThrowIfNull(neutralPath);

            // A culture name has no dot (ar, zh-Hant), so Strings.Designer.resx-like names with two
            // further segments are not satellites.
            string directory = Path.GetDirectoryName(Path.GetFullPath(neutralPath)) ?? string.Empty;
            string prefix = Path.GetFileNameWithoutExtension(neutralPath) + ".";
            return [.. Directory.EnumerateFiles(directory, prefix + "*" + Extension)
                .Select(file => (Culture: Path.GetFileName(file)[prefix.Length..^Extension.Length], Path: file))
                .Where(static satellite => satellite.Culture.Length > 0 && !satellite.Culture.Contains('.', StringComparison.Ordinal))
                .OrderBy(static satellite => satellite.Culture, StringComparer.Ordinal)];
        }
    }
}
