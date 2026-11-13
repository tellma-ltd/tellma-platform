// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Reflection;
using System.Resources;
using Tellma.Core.Composition;

namespace Tellma.Core.Localization
{
    // The resource convention is spec 0013 §10.1's: Resources/Strings.resx in the assembly's root
    // namespace, neutral = English.
    /// <summary>
    ///     The keys of the neutral <c>Strings</c> resources of every composed feature's assembly, read
    ///     once, on first use, from the assemblies' manifests.
    /// </summary>
    /// <param name="record">The realised composition, whose features name the assemblies.</param>
    internal sealed class NeutralStringKeys(TellmaCompositionRecord record) : INeutralStringKeys
    {
        /// <summary>The end of the manifest name of an assembly's neutral shared resources.</summary>
        private const string ManifestSuffix = ".Resources.Strings.resources";

        private readonly Lazy<HashSet<string>> _keys = new(() => Read(record), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public bool Declares(string key)
        {
            return key is not null && _keys.Value.Contains(key);
        }

        /// <summary>Reads the keys of every composed feature assembly's neutral shared resources.</summary>
        /// <param name="record">The realised composition.</param>
        /// <returns>The keys.</returns>
        private static HashSet<string> Read(TellmaCompositionRecord record)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (Assembly assembly in record.Features.Select(static feature => feature.GetType().Assembly).Distinct())
            {
                // A satellite carries a culture's translations under its own manifest name, so the
                // neutral set is the one embedded in the feature's own assembly.
                foreach (string name in assembly.GetManifestResourceNames().Where(static name => name.EndsWith(ManifestSuffix, StringComparison.Ordinal)))
                {
                    using Stream? stream = assembly.GetManifestResourceStream(name);
                    if (stream is null)
                    {
                        continue;
                    }

                    // The keys alone: no value is read, so nothing is deserialized.
                    using ResourceReader reader = new(stream);
                    IDictionaryEnumerator entries = reader.GetEnumerator();
                    while (entries.MoveNext())
                    {
                        if (entries.Key is string key)
                        {
                            _ = keys.Add(key);
                        }
                    }
                }
            }

            return keys;
        }
    }
}
