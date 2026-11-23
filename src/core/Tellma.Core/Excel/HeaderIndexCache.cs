// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Localization;

namespace Tellma.Core.Excel
{
    /// <summary>The sheet names a workbook's sheets are matched by, each with the collection path it names.</summary>
    internal sealed class SheetIndex
    {
        /// <summary>The names that name one target, without regard to case.</summary>
        private readonly Dictionary<string, string> _names;

        /// <summary>Creates the index.</summary>
        /// <param name="names">The names that name one target.</param>
        private SheetIndex(Dictionary<string, string> names)
        {
            _names = names;
        }

        /// <summary>Merges candidate names into an index: a name every candidate gives one target keeps it, any other name matches nothing.</summary>
        /// <param name="candidates">Each name with its collection path, <c>""</c> for the parent sheet.</param>
        /// <returns>The index.</returns>
        public static SheetIndex Merge(IEnumerable<(string Name, string Collection)> candidates)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> colliding = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, string collection) in candidates)
            {
                if (colliding.Contains(name))
                {
                    continue;
                }

                if (names.TryGetValue(name, out string? existing))
                {
                    if (!string.Equals(existing, collection, StringComparison.Ordinal))
                    {
                        _ = names.Remove(name);
                        _ = colliding.Add(name);
                    }
                }
                else
                {
                    names[name] = collection;
                }
            }

            return new SheetIndex(names);
        }

        /// <summary>Matches a sheet's name, without regard to case.</summary>
        /// <param name="name">The sheet's name.</param>
        /// <param name="collection">The collection path it names, <c>""</c> for the parent sheet.</param>
        /// <returns>True when the name names exactly one target.</returns>
        public bool TryMatch(string name, out string collection)
        {
            ArgumentNullException.ThrowIfNull(name);
            bool found = _names.TryGetValue(name, out string? match);
            collection = match ?? string.Empty;
            return found;
        }
    }

    // Spec 0019 §8.1 steps 1 and 2: the headers and sheet names an import matches are those every
    // export could write, in the request culture, each tenant language and English. Labels resolve
    // under the current culture (spec 0013 §7.6), so each culture's candidates are rendered inside a
    // localization scope of that culture with no content languages, which renders a multilingual
    // group's label bare; the symbols are appended here for every language of the catalogue. One
    // culture's candidates depend on the entity and the culture alone, so they are cached by those,
    // and the merged index by the entity and the request's list of cultures, which the tenant's
    // languages and the request culture fix.
    /// <summary>
    ///     Builds and caches, for the life of the process, the header index of each sheet's entity
    ///     and the sheet-name index of each stack's entity, per list of candidate cultures.
    /// </summary>
    internal sealed class HeaderIndexCache
    {
        /// <summary>The most entries each of the caches holds; a full cache is emptied before the next entry.</summary>
        public const int MaxEntries = 1024;

        /// <summary>The language every import matches in, beside the request's and the tenant's.</summary>
        public const string English = "en";

        /// <summary>The separator of the parts of a cache key, which no entity, property or language name holds.</summary>
        private const char KeySeparator = '\u001F';

        /// <summary>Each culture's header candidates, by entity, parent key and language.</summary>
        private readonly ConcurrentDictionary<string, IReadOnlyList<HeaderCandidate>> _candidates = new(StringComparer.Ordinal);

        /// <summary>The merged header indexes, by entity, parent key and list of languages.</summary>
        private readonly ConcurrentDictionary<string, HeaderIndex> _headers = new(StringComparer.Ordinal);

        /// <summary>The merged sheet-name indexes, by entity and list of languages.</summary>
        private readonly ConcurrentDictionary<string, SheetIndex> _sheets = new(StringComparer.Ordinal);

        /// <summary>The header index of a sheet's entity.</summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="context">The request's context: its culture, the tenant's languages, the labels and the catalogue.</param>
        /// <returns>The index.</returns>
        public HeaderIndex Headers(EntityMetadata entity, ChildCollectionMetadata? collection, ExcelContext context)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(context);
            IReadOnlyList<(string Language, CultureInfo Culture)> cultures = Cultures(context);
            string parentKey = collection?.ParentKey.Name ?? string.Empty;
            string key = string.Join(KeySeparator, [entity.Name, parentKey, .. cultures.Select(static culture => culture.Language)]);
            return GetOrAdd(_headers, key, () => HeaderIndex.Merge(cultures.SelectMany(culture => GetOrAdd(
                _candidates,
                string.Join(KeySeparator, entity.Name, parentKey, culture.Language),
                () => Rendered(culture.Culture, context, () => HeaderCandidates.Of(entity, collection, context.Labels, context.Catalog.All))))));
        }

        /// <summary>The sheet-name index of a stack's entity: its own sheet's names and each collection's.</summary>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="collections">The entity's collection paths.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The index.</returns>
        public SheetIndex Sheets(EntityMetadata entity, IReadOnlyList<ImportCollection> collections, ExcelContext context)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(collections);
            ArgumentNullException.ThrowIfNull(context);
            IReadOnlyList<(string Language, CultureInfo Culture)> cultures = Cultures(context);
            string key = string.Join(KeySeparator, [entity.Name, .. cultures.Select(static culture => culture.Language)]);
            return GetOrAdd(_sheets, key, () =>
            {
                // --- Each culture's generated names, then the technical collection paths ---
                List<(string Name, string Collection)> names = [];
                foreach ((_, CultureInfo culture) in cultures)
                {
                    names.AddRange(Rendered(culture, context, () => SheetNamesOf(entity, collections, context.Labels)));
                }

                names.AddRange(collections.Select(static collection => (SheetNames.Scrub(collection.Path), collection.Path)));
                return SheetIndex.Merge(names);
            });
        }

        /// <summary>The candidate cultures of a request: its own, each tenant language's and English, each language once.</summary>
        /// <param name="context">The request's context.</param>
        /// <returns>Each language with the culture its labels render in.</returns>
        internal static IReadOnlyList<(string Language, CultureInfo Culture)> Cultures(ExcelContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            List<(string Language, CultureInfo Culture)> cultures = [];
            Add(LanguageOf(context.Culture), context.Culture);
            foreach (string language in context.TenantLanguages)
            {
                Add(language, null);
            }

            Add(English, null);
            return cultures;

            // A language the catalogue lacks has no culture of its own, and no labels to match.
            void Add(string language, CultureInfo? culture)
            {
                if (cultures.Any(entry => string.Equals(entry.Language, language, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                culture ??= context.Catalog.Find(language) is null ? null : NegotiatedCultures.Get(language, context.Catalog.Formats(language).DefaultRegion);
                if (culture is not null)
                {
                    cultures.Add((language, culture));
                }
            }
        }

        /// <summary>The language a culture's labels render in: the bare language subtag of its name, English for the invariant culture.</summary>
        /// <param name="culture">The culture.</param>
        /// <returns>The language code.</returns>
        private static string LanguageOf(CultureInfo culture)
        {
            string name = culture.Name;
            int dash = name.IndexOf('-', StringComparison.Ordinal);
            return name.Length == 0 ? English : dash < 0 ? name : name[..dash];
        }

        /// <summary>Renders under a localization scope of a culture and no content languages.</summary>
        /// <typeparam name="T">What is rendered.</typeparam>
        /// <param name="culture">The culture.</param>
        /// <param name="context">The request's context, whose calendar and time zone the scope carries.</param>
        /// <param name="render">Renders.</param>
        /// <returns>What was rendered.</returns>
        private static T Rendered<T>(CultureInfo culture, ExcelContext context, Func<T> render)
        {
            using (LocalizationScope.Enter(new LocalizationContext(culture, context.Calendar, context.TimeZone, 0, [], LanguageOf(culture), culture.Name)))
            {
                return render();
            }
        }

        /// <summary>The sheet names one culture's export writes: the parent's plural label, and each collection's name under it.</summary>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="collections">Its collection paths.</param>
        /// <param name="labels">The labels, rendering in the scope's culture.</param>
        /// <returns>Each name with its collection path.</returns>
        private static List<(string Name, string Collection)> SheetNamesOf(EntityMetadata entity, IReadOnlyList<ImportCollection> collections, ILabelProvider labels)
        {
            string parent = labels.EntityLabel(entity.ClrType, plural: true);
            List<(string Name, string Collection)> names = [(SheetNames.Scrub(parent), string.Empty)];
            foreach (ImportCollection collection in collections)
            {
                string[] segments = [parent, .. collection.Labels.Select(label => labels.CollectionLabel(label.Owner.ClrType, label.Collection))];
                names.Add((SheetNames.Scrub(ExportPlanner.ChildSheetName(segments)), collection.Path));
            }

            return names;
        }

        /// <summary>Gets a cached entry, building it once; a full cache is emptied first.</summary>
        /// <typeparam name="T">The entry's type.</typeparam>
        /// <param name="cache">The cache.</param>
        /// <param name="key">The entry's key.</param>
        /// <param name="build">Builds the entry.</param>
        /// <returns>The entry.</returns>
        private static T GetOrAdd<T>(ConcurrentDictionary<string, T> cache, string key, Func<T> build)
        {
            if (cache.TryGetValue(key, out T? found))
            {
                return found;
            }

            if (cache.Count >= MaxEntries)
            {
                cache.Clear();
            }

            return cache.GetOrAdd(key, _ => build());
        }
    }
}
