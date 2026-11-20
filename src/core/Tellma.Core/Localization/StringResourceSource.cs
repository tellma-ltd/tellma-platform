// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Tellma.Core.Localization
{
    // The resource convention is spec 0013 §10.1's: Resources/Strings.resx in the assembly's root
    // namespace, neutral = English, satellites Strings.<lang>.resx.
    /// <summary>
    ///     One assembly's shared <c>Strings</c> resources, read one culture at a time: the neutral
    ///     (English) set, or a language's own satellite set without its parents.
    /// </summary>
    /// <param name="Name">The resources' base name (<c>Tellma.Core.Resources.Strings</c>), which names them in a conflict.</param>
    /// <param name="Read">
    ///     Reads a culture's own entries: the neutral set for the invariant culture, a satellite's
    ///     set for a language, null when the assembly ships none for it. Only string values are
    ///     entries.
    /// </param>
    internal sealed record StringResourceSource(string Name, Func<CultureInfo, IReadOnlyDictionary<string, string>?> Read)
    {
        /// <summary>The end of the manifest name of an assembly's neutral shared resources.</summary>
        public const string ManifestSuffix = ".Resources.Strings.resources";

        /// <summary>The end of every compiled resource's manifest name, which the base name drops.</summary>
        private const string ResourcesExtension = ".resources";

        /// <summary>
        ///     The name of the assembly that embeds the resources, which a localizer is created from
        ///     beside <see cref="Name" />; null for a source the suites build in memory.
        /// </summary>
        public string? AssemblyName { get; init; }

        /// <summary>
        ///     The shared <c>Strings</c> resources of each assembly, in the assemblies' order: one
        ///     source per neutral <c>…Resources.Strings.resources</c> manifest an assembly embeds,
        ///     which leaves out every <c>ServerStrings</c> set.
        /// </summary>
        /// <param name="assemblies">The assemblies, each read once.</param>
        /// <returns>The sources.</returns>
        public static IReadOnlyList<StringResourceSource> Of(IEnumerable<Assembly> assemblies)
        {
            ArgumentNullException.ThrowIfNull(assemblies);

            List<StringResourceSource> sources = [];
            foreach (Assembly assembly in assemblies.Distinct())
            {
                // A satellite carries a culture's translations under its own manifest name, so the
                // neutral set is the one embedded in the feature's own assembly; its base name is
                // what the resource manager looks the satellites up by.
                foreach (string manifest in assembly.GetManifestResourceNames()
                    .Where(static name => name.EndsWith(ManifestSuffix, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal))
                {
                    string baseName = manifest[..^ResourcesExtension.Length];
                    ResourceManager manager = new(baseName, assembly);
                    sources.Add(new StringResourceSource(baseName, culture => ReadSet(manager, culture)) { AssemblyName = assembly.GetName().Name });
                }
            }

            return sources;
        }

        /// <summary>A culture's own resource set, without its parents, as a dictionary of its string entries.</summary>
        /// <param name="manager">The resources' manager.</param>
        /// <param name="culture">The culture; the invariant culture for the neutral set.</param>
        /// <returns>The entries, or null when no set ships for the culture.</returns>
        private static FrozenDictionary<string, string>? ReadSet(ResourceManager manager, CultureInfo culture)
        {
            // tryParents: false, so a language's set is its satellite's own entries and never the
            // neutral text a lookup would fall back to.
            ResourceSet? set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            if (set is null)
            {
                return null;
            }

            Dictionary<string, string> entries = new(StringComparer.Ordinal);
            IDictionaryEnumerator enumerator = set.GetEnumerator();
            while (enumerator.MoveNext())
            {
                // A non-string resource (an image, a file) is never text the pack can carry.
                if (enumerator.Key is string key && enumerator.Value is string value)
                {
                    entries[key] = value;
                }
            }

            return entries.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }
}
