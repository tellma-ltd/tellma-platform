// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The lowering is spec 0015 §5.2.1; the display name is spec 0012 §2.5.
    /// <summary>
    ///     Lowers a search text into one disjunction over a root's columns: the searchable columns
    ///     (or the service's own branch in their place), the id, and the display name, its prefix
    ///     and its <c>prefix - source</c> split. The text reaches the query only as declared
    ///     parameters, so the filter takes one of a few constant shapes per root.
    /// </summary>
    internal static class QuerySearch
    {
        /// <summary>The parameter carrying the search text.</summary>
        public const string SearchParameter = "search";

        /// <summary>The parameter carrying the text read as an integer.</summary>
        public const string IdParameter = "searchId";

        /// <summary>The parameter carrying the split's prefix side.</summary>
        public const string LeftParameter = "left";

        /// <summary>The parameter carrying the split's source side.</summary>
        public const string RightParameter = "right";

        /// <summary>The separator of a display name's prefix and source, which the split looks for.</summary>
        private const string Separator = DisplayNameMetadata.Separator;

        /// <summary>The parameters a search declares, which a caller's clauses may not mention while a search applies.</summary>
        public static IReadOnlySet<string> Parameters { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SearchParameter, IdParameter, LeftParameter, RightParameter,
        };

        /// <summary>Lowers a search text.</summary>
        /// <param name="surface">The surface the query runs on.</param>
        /// <param name="root">The root, of the tenant's schema, which admits the multilingual twins its languages use.</param>
        /// <param name="text">The search text, its bidi controls stripped; never empty.</param>
        /// <param name="custom">The service's branch replacing the searchable columns'; null for the platform's.</param>
        /// <returns>The filter and the parameters it binds.</returns>
        public static SearchFilter Lower(QuerySurface surface, EntityDescriptor root, string text, FilterTree? custom)
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(root);
            ArgumentException.ThrowIfNullOrEmpty(text);

            Branches branches = new();
            long? id = null;

            // --- The searchable columns, or the service's branch in their place ---
            if (custom is not null)
            {
                branches.Add(custom);
            }
            else
            {
                foreach ((string property, SearchKind kind) in surface.Searchable)
                {
                    if (SchemaLookup.Property(root, property) is { Type: QueryexType.QxString } column)
                    {
                        branches.Add(Match(kind, column.Name), readsSearch: true);
                    }
                }
            }

            // --- The id: an integer, or # and an integer, that the key can hold ---
            string trimmed = text.Trim();
            string idText = trimmed.StartsWith('#') ? trimmed[1..] : trimmed;
            if (TryParseInteger(idText, out long key) && Fits(key, surface.KeyType))
            {
                branches.Add($"{root.Key.Name} = @{IdParameter}");
                id = key;
            }

            // --- The display name: its source over every admitted slot, then its prefix ---
            DisplayNameMetadata display = surface.DisplayName;
            List<(PropertyMetadata Metadata, string Name)> slots = Slots(display, root);
            bool stringSource = IsString(display.Source.ClrType);
            if (stringSource)
            {
                foreach ((PropertyMetadata slot, string name) in slots)
                {
                    branches.Add(Match(KindOf(slot), name), readsSearch: true);
                }
            }
            else if (IsInteger(display.Source.ClrType) && slots.Count > 0 && TryParseNormalized(trimmed, out long sourceValue) && Fits(sourceValue, display.Source.ClrType))
            {
                // An Arabic-Indic or Latin text reads one number, the same the id branch reads when both apply.
                branches.Add($"{slots[0].Name} = @{IdParameter}");
                id = sourceValue;
            }

            object? left = null;
            string? right = null;
            if (display.Prefix is PropertyMetadata prefix && SchemaLookup.Property(root, prefix.Name) is PropertyDescriptor prefixColumn)
            {
                if (IsString(prefix.ClrType))
                {
                    branches.Add(Match(KindOf(prefix), prefixColumn.Name), readsSearch: true);
                }
                else if (IsInteger(prefix.ClrType) && TryParseNormalized(trimmed, out long prefixValue) && Fits(prefixValue, prefix.ClrType))
                {
                    branches.Add($"{prefixColumn.Name} = @{IdParameter}");
                    id = prefixValue;
                }

                // --- The split: "prefix - source", the source's slots matched from their start ---
                int at = text.IndexOf(Separator, StringComparison.Ordinal);
                if (stringSource && slots.Count > 0 && at >= 0 && SplitPrefix(text[..at].Trim(), prefix.ClrType) is object prefixSide)
                {
                    string sources = string.Join(" or ", slots.Select(static slot => $"startsWith({slot.Name}, @{RightParameter})"));
                    branches.Add($"{prefixColumn.Name} = @{LeftParameter} and ({sources})");
                    left = prefixSide;
                    right = text[(at + Separator.Length)..].Trim();
                }
            }

            return branches.Build(text, id, left, right);
        }

        /// <summary>The admitted slots of a display name's source: its group's twins the schema declares, or the source itself.</summary>
        /// <param name="display">The display-name rule.</param>
        /// <param name="root">The root.</param>
        /// <returns>The slots, each with its name as the schema declares it.</returns>
        private static List<(PropertyMetadata Metadata, string Name)> Slots(DisplayNameMetadata display, EntityDescriptor root)
        {
            IEnumerable<PropertyMetadata> candidates = display.Group is MultilingualGroup group
                ? ((PropertyMetadata?[])[group.Primary, group.Secondary, group.Tertiary]).OfType<PropertyMetadata>()
                : [display.Source];
            List<(PropertyMetadata, string)> slots = [];
            foreach (PropertyMetadata candidate in candidates)
            {
                if (SchemaLookup.Property(root, candidate.Name) is PropertyDescriptor column)
                {
                    slots.Add((candidate, column.Name));
                }
            }

            return slots;
        }

        /// <summary>The split's prefix side as its parameter binds it, or null when it cannot match.</summary>
        /// <param name="text">The trimmed text before the separator.</param>
        /// <param name="type">The prefix's type.</param>
        /// <returns>The value: the text for a string prefix, the number for an integer one.</returns>
        private static object? SplitPrefix(string text, Type type)
        {
            return text.Length == 0
                ? null
                : IsString(type)
                    ? text
                    : IsInteger(type) && TryParseNormalized(text, out long value) && Fits(value, type) ? (decimal)value : null;
        }

        /// <summary>A searchable column's match: <c>contains</c>, or <c>startsWith</c> for a prefix column.</summary>
        /// <param name="kind">The column's search kind.</param>
        /// <param name="column">The column's name.</param>
        /// <returns>The predicate's text.</returns>
        private static string Match(SearchKind kind, string column)
        {
            string function = kind == SearchKind.Prefix ? "startsWith" : "contains";
            return $"{function}({column}, @{SearchParameter})";
        }

        /// <summary>A display-name column's search kind: its own when it is searchable, else contains.</summary>
        /// <param name="property">The column.</param>
        /// <returns>The kind.</returns>
        private static SearchKind KindOf(PropertyMetadata property)
        {
            return property.IsSearchable ? property.SearchKind ?? SearchKind.Contains : SearchKind.Contains;
        }

        /// <summary>Parses <c>-?[0-9]+</c> in Latin digits.</summary>
        /// <param name="text">The text.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        internal static bool TryParseInteger(string text, out long value)
        {
            value = 0;
            ReadOnlySpan<char> digits = text.StartsWith('-') ? text.AsSpan(1) : text.AsSpan();
            return digits.Length > 0
                && !digits.ContainsAnyExceptInRange('0', '9')
                && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Parses an integer whose Arabic-Indic and Extended Arabic-Indic digits read as Latin ones.</summary>
        /// <param name="text">The text.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        internal static bool TryParseNormalized(string text, out long value)
        {
            // U+0660–U+0669 and U+06F0–U+06F9 are the two Arabic digit runs, each in digit order.
            Span<char> latin = text.Length <= 64 ? stackalloc char[text.Length] : new char[text.Length];
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                latin[index] = c switch
                {
                    >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                    >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                    _ => c,
                };
            }

            return TryParseInteger(new string(latin), out value);
        }

        /// <summary>Whether a value fits an integral type.</summary>
        /// <param name="value">The value.</param>
        /// <param name="type">The type, nullable or not.</param>
        /// <returns>True when the type holds the value.</returns>
        internal static bool Fits(long value, Type type)
        {
            Type plain = Nullable.GetUnderlyingType(type) ?? type;
            return plain == typeof(long)
                || (plain == typeof(int) && value is >= int.MinValue and <= int.MaxValue)
                || (plain == typeof(short) && value is >= short.MinValue and <= short.MaxValue)
                || (plain == typeof(byte) && value is >= byte.MinValue and <= byte.MaxValue);
        }

        /// <summary>Whether a property type is an integer the display name compares by equality.</summary>
        /// <param name="type">The type, nullable or not.</param>
        /// <returns>True for an integer.</returns>
        private static bool IsInteger(Type type)
        {
            Type plain = Nullable.GetUnderlyingType(type) ?? type;
            return plain == typeof(long) || plain == typeof(int) || plain == typeof(short) || plain == typeof(byte);
        }

        /// <summary>Whether a property type is text.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True for a string.</returns>
        private static bool IsString(Type type)
        {
            return type == typeof(string);
        }

        /// <summary>The disjuncts gathered so far, each once.</summary>
        private sealed class Branches
        {
            /// <summary>The predicate texts added, to skip one added twice.</summary>
            private readonly HashSet<string> _texts = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>The disjuncts, in order.</summary>
            private readonly List<FilterTree> _trees = [];

            /// <summary>Whether a disjunct reads the search text.</summary>
            private bool _readsSearch;

            /// <summary>Adds a predicate, unless an equal one is already there.</summary>
            /// <param name="text">The predicate's text.</param>
            /// <param name="readsSearch">Whether the predicate reads the search text.</param>
            public void Add(string text, bool readsSearch = false)
            {
                if (_texts.Add(text))
                {
                    _trees.Add(FilterTree.Leaf(text));
                    _readsSearch |= readsSearch;
                }
            }

            /// <summary>Adds the service's own branch, which may read the search text.</summary>
            /// <param name="tree">The branch.</param>
            public void Add(FilterTree tree)
            {
                _trees.Add(tree);
                _readsSearch = true;
            }

            /// <summary>The filter and its parameters.</summary>
            /// <param name="text">The search text.</param>
            /// <param name="id">The integer the id and integer display-name branches compare with; null when none applies.</param>
            /// <param name="left">The split's prefix side; null when the split does not apply.</param>
            /// <param name="right">The split's source side; null when the split does not apply.</param>
            /// <returns>The filter.</returns>
            public SearchFilter Build(string text, long? id, object? left, string? right)
            {
                // A search no branch applies to matches no row.
                if (_trees.Count == 0)
                {
                    return new SearchFilter(FilterTree.Leaf("false"), []);
                }

                List<(QueryexParameterDeclaration, object?)> parameters = [];
                if (_readsSearch)
                {
                    parameters.Add((new QueryexParameterDeclaration(SearchParameter, QueryexType.QxString, IsNotNull: false), text));
                }

                if (id is long value)
                {
                    parameters.Add((new QueryexParameterDeclaration(IdParameter, QueryexType.QxNumeric, IsNotNull: false), (decimal)value));
                }

                if (left is not null)
                {
                    QueryexType type = left is string ? QueryexType.QxString : QueryexType.QxNumeric;
                    parameters.Add((new QueryexParameterDeclaration(LeftParameter, type, IsNotNull: false), left));
                    parameters.Add((new QueryexParameterDeclaration(RightParameter, QueryexType.QxString, IsNotNull: false), right));
                }

                return new SearchFilter(FilterTree.Or(_trees), parameters);
            }
        }
    }

    /// <summary>A lowered search: its disjunction and the parameters the disjunction binds.</summary>
    /// <param name="Tree">The disjunction.</param>
    /// <param name="Parameters">Each parameter's declaration and value.</param>
    internal sealed record SearchFilter(FilterTree Tree, IReadOnlyList<(QueryexParameterDeclaration Declaration, object? Value)> Parameters);
}
