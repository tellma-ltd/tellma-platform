// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Composition;

namespace Tellma.Core.Localization
{
    // The pack is spec 0013 §10.4's; the labels it serves are §7.6's.
    /// <summary>
    ///     Merges the shared <c>Strings</c> resources of every assembly that declares a composed
    ///     feature (Core, each pack, the distribution) into one dictionary per language, and
    ///     serializes each into a <see cref="StringPack" />: a flat JSON object, resource keys
    ///     verbatim. The label provider resolves every label against the same dictionaries, so no
    ///     lookup names an assembly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A language's dictionary holds that language's own entries: the neutral set for
    ///         English, each assembly's satellite for any other language, never a parent's text, so
    ///         a key a language lacks falls through to the English pack. English always gets a pack,
    ///         offered or not, because it is every other pack's fallback.
    ///     </para>
    ///     <para>
    ///         A key has one owner, the first assembly in composition order whose neutral set
    ///         declares it, and only the owner's translations of it are merged. Another assembly
    ///         declaring the key, in its neutral set or in a satellite, is a conflict: the startup
    ///         gate refuses the composition with every conflict listed. A translation of a key no
    ///         neutral set declares is left out, since no label reads it. ServerStrings resources
    ///         are never read.
    ///     </para>
    ///     <para>Composed once, on first use, and never changed.</para>
    /// </remarks>
    internal sealed class StringPackComposer : IStringPackProvider, INeutralStringKeys
    {
        /// <summary>The pack writer's options: the platform's encoder, so a pack spells text as every other payload does.</summary>
        private static readonly JsonWriterOptions WriterOptions = new() { Encoder = TellmaJson.Options.Encoder };

        private readonly Lazy<ComposedPacks> _composition;

        /// <summary>Creates the composer of a realised composition.</summary>
        /// <param name="record">The realised composition, whose features name the assemblies.</param>
        /// <param name="catalog">The language catalogue, whose offered languages each get a pack.</param>
        /// <param name="versions">The deployment's versions, whose distribution version every pack carries.</param>
        public StringPackComposer(TellmaCompositionRecord record, ILanguageCatalog catalog, DeploymentVersions versions)
            : this(
                StringResourceSource.Of((record ?? throw new ArgumentNullException(nameof(record))).Features.Select(static feature => feature.GetType().Assembly)),
                (catalog ?? throw new ArgumentNullException(nameof(catalog))).Offered.Select(static language => language.Code),
                (versions ?? throw new ArgumentNullException(nameof(versions))).DistributionVersion)
        {
        }

        /// <summary>Creates a composer over given sources, which the suites build in memory.</summary>
        /// <param name="sources">The sources, in composition order: an earlier source keeps a conflicting key.</param>
        /// <param name="offered">The offered language codes.</param>
        /// <param name="version">The version every pack carries.</param>
        internal StringPackComposer(IReadOnlyList<StringResourceSource> sources, IEnumerable<string> offered, string version)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(offered);
            ArgumentException.ThrowIfNullOrWhiteSpace(version);

            // English first, so every key's owner is known before any translation is merged; then
            // the offered languages in declaration order.
            string[] languages = [.. new[] { LanguageSet.English }.Concat(offered).Distinct(StringComparer.OrdinalIgnoreCase)];
            _composition = new(() => Compose(sources, languages, version), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>Every pack, English first, then the offered languages in declaration order.</summary>
        public IReadOnlyList<StringPack> Packs => _composition.Value.Packs;

        /// <summary>The keys two sources both declare, in the order they were found; empty for a sound composition.</summary>
        public IReadOnlyList<StringKeyConflict> Conflicts => _composition.Value.Conflicts;

        /// <summary>The English (neutral) merged dictionary, which decides the key every label resolves through.</summary>
        public IReadOnlyDictionary<string, string> English => _composition.Value.Dictionaries[LanguageSet.English];

        /// <inheritdoc />
        public StringPack? Get(string language)
        {
            ArgumentNullException.ThrowIfNull(language);
            return _composition.Value.PacksByLanguage.GetValueOrDefault(language);
        }

        /// <summary>A language's merged dictionary: its own entries from every source.</summary>
        /// <param name="language">The language code, compared without regard to case.</param>
        /// <returns>The dictionary, or null for a language that has no pack.</returns>
        public IReadOnlyDictionary<string, string>? Strings(string language)
        {
            ArgumentNullException.ThrowIfNull(language);
            return _composition.Value.Dictionaries.GetValueOrDefault(language);
        }

        /// <inheritdoc />
        public bool Declares(string key)
        {
            return key is not null && English.ContainsKey(key);
        }

        /// <summary>Merges the sources for each language and serializes each merged dictionary.</summary>
        /// <param name="sources">The sources, in composition order.</param>
        /// <param name="languages">The languages, English first.</param>
        /// <param name="version">The version every pack carries.</param>
        /// <returns>The composition.</returns>
        private static ComposedPacks Compose(IReadOnlyList<StringResourceSource> sources, string[] languages, string version)
        {
            Dictionary<string, FrozenDictionary<string, string>> dictionaries = new(StringComparer.OrdinalIgnoreCase);
            List<StringPack> packs = [];
            List<StringKeyConflict> conflicts = [];
            HashSet<(string Key, string First, string Second)> reported = [];

            // The owner of each key: the first source whose neutral (English) set declares it. The
            // English pass fills it; every other language's pass only reads it.
            Dictionary<string, string> owners = new(StringComparer.Ordinal);

            foreach (string language in languages)
            {
                // --- Merge: each source's own entries for the language, each key from its owner alone ---
                bool neutral = string.Equals(language, LanguageSet.English, StringComparison.OrdinalIgnoreCase);
                CultureInfo? culture = Culture(language);
                Dictionary<string, string> merged = new(StringComparer.Ordinal);
                foreach (StringResourceSource source in sources)
                {
                    IReadOnlyDictionary<string, string>? entries = culture is null ? null : source.Read(culture);
                    foreach ((string key, string value) in entries ?? FrozenDictionary<string, string>.Empty)
                    {
                        if (neutral && owners.TryAdd(key, source.Name))
                        {
                            merged[key] = value;
                            continue;
                        }

                        if (!owners.TryGetValue(key, out string? owner))
                        {
                            // A translation of a key no neutral set declares: no label reads it, so
                            // it stays out of the pack (a feature's resource audit names it).
                            continue;
                        }

                        if (string.Equals(owner, source.Name, StringComparison.Ordinal))
                        {
                            merged[key] = value;
                            continue;
                        }

                        // Another source's key, whether redeclared or translated: one conflict per
                        // key and pair of sources, whichever languages repeat it.
                        if (reported.Add((key, owner, source.Name)))
                        {
                            conflicts.Add(new StringKeyConflict(key, owner, source.Name));
                        }
                    }
                }

                // --- Serialize: one flat object, keys in ordinal order so a pack's bytes depend on its content alone ---
                var dictionary = merged.ToFrozenDictionary(StringComparer.Ordinal);
                dictionaries[language] = dictionary;
                packs.Add(new StringPack(language, version, Serialize(dictionary)));
            }

            return new ComposedPacks(
                packs,
                packs.ToFrozenDictionary(static pack => pack.Language, StringComparer.OrdinalIgnoreCase),
                dictionaries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                conflicts);
        }

        /// <summary>The culture whose own resource sets a language's dictionary merges.</summary>
        /// <param name="language">The language code.</param>
        /// <returns>The invariant culture for English, whose sets are the neutral ones; null for a code this host cannot name, which no satellite ships for.</returns>
        private static CultureInfo? Culture(string language)
        {
            if (string.Equals(language, LanguageSet.English, StringComparison.OrdinalIgnoreCase))
            {
                return CultureInfo.InvariantCulture;
            }

            try
            {
                return CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                // The languages check reports the missing strings of such a language; its pack is empty.
                return null;
            }
        }

        /// <summary>A merged dictionary as one flat JSON object, its keys in ordinal order.</summary>
        /// <param name="dictionary">The dictionary.</param>
        /// <returns>The UTF-8 JSON.</returns>
        private static byte[] Serialize(IReadOnlyDictionary<string, string> dictionary)
        {
            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                foreach ((string key, string value) in dictionary.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(key, value);
                }

                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }

        /// <summary>What the composer built.</summary>
        /// <param name="Packs">The packs, English first.</param>
        /// <param name="PacksByLanguage">The packs by language, compared without regard to case.</param>
        /// <param name="Dictionaries">The merged dictionaries by language, compared without regard to case.</param>
        /// <param name="Conflicts">The conflicting keys.</param>
        private sealed record ComposedPacks(
            IReadOnlyList<StringPack> Packs,
            FrozenDictionary<string, StringPack> PacksByLanguage,
            FrozenDictionary<string, FrozenDictionary<string, string>> Dictionaries,
            IReadOnlyList<StringKeyConflict> Conflicts);
    }

    /// <summary>
    ///     A shared string resource key two composed assemblies both declare: in both neutral sets, or
    ///     in one's neutral set and in the other's translation.
    /// </summary>
    /// <param name="Key">The key.</param>
    /// <param name="First">The resources that keep it: the first in composition order whose neutral set declares it.</param>
    /// <param name="Second">The resources whose entry is dropped.</param>
    internal sealed record StringKeyConflict(string Key, string First, string Second);
}
