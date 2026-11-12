// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Resources;

namespace Tellma.Core.Data.Queries
{
    // The rendering rule is spec 0029 §5.2's, the columns spec 0012 §2.5's; the client renders the
    // same rule, and display-name-golden.json pins both sides.
    /// <summary>The server renderer of display names, over the model's entity metadata.</summary>
    /// <param name="metadata">The model's entity metadata.</param>
    internal sealed class DisplayNames(IEntityMetadataProvider metadata) : IDisplayNames
    {
        /// <summary>Each entity's rule, by entity name, built on first use.</summary>
        private readonly Lazy<FrozenDictionary<string, DisplayNameRule>> _rules = new(
            () => metadata.All.ToFrozenDictionary(static entity => entity.Name, DisplayNameRule.Of, StringComparer.Ordinal),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public IReadOnlyList<string> Columns(string entity, MultilingualShape shape, string? navigationPath = null)
        {
            return Rule(entity).Columns(shape, navigationPath);
        }

        /// <inheritdoc />
        public string Render(string entity, IReadOnlyList<object?> values, int contentSlot, CultureInfo culture, ICalendarSystem calendar)
        {
            return Rule(entity).Render(values, contentSlot, culture, calendar);
        }

        /// <summary>The rule of an entity.</summary>
        /// <param name="entity">The entity's name.</param>
        /// <returns>The rule.</returns>
        /// <exception cref="ArgumentException">No entity has the name.</exception>
        private DisplayNameRule Rule(string entity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entity);
            return _rules.Value.GetValueOrDefault(entity)
                ?? throw new ArgumentException($"No entity of the model is named '{entity}'.", nameof(entity));
        }
    }

    /// <summary>
    ///     One entity's display-name rule: its key, its source (a multilingual group's twins or one
    ///     scalar) and its optional prefix, by property name.
    /// </summary>
    internal sealed class DisplayNameRule
    {
        /// <summary>The bidirectional control characters a display name never carries.</summary>
        private static readonly FrozenSet<char> BidiControls =
        [
            '؜', '‎', '‏', '‪', '‫', '‬', '‭', '‮', '⁦', '⁧', '⁨', '⁩',
        ];

        /// <summary>The resource key of the text a row with neither a name nor a prefix renders as.</summary>
        private const string FallbackKey = "Entity_DisplayNameFallback";

        /// <summary>The placeholder of the id in the fallback text.</summary>
        private const string IdPlaceholder = "{id}";

        /// <summary>The column layout under each shape, by shape.</summary>
        private readonly FrozenDictionary<MultilingualShape, Layout> _layouts;

        /// <summary>Creates a rule.</summary>
        /// <param name="key">The key property's name.</param>
        /// <param name="twins">A group source's twins in slot order, the absent ones null; null for a scalar source.</param>
        /// <param name="source">A scalar source's property name; ignored for a group source.</param>
        /// <param name="prefix">The prefix property's name, or null for none.</param>
        public DisplayNameRule(string key, IReadOnlyList<string?>? twins, string? source, string? prefix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (twins is null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(source);
            }
            else if (twins.Count is < 1 or > 3 || string.IsNullOrWhiteSpace(twins[0]))
            {
                throw new ArgumentException("A group source has one to three twins, the first always present.", nameof(twins));
            }

            _layouts = Enum.GetValues<MultilingualShape>().ToFrozenDictionary(static shape => shape, shape => Layout.Of(shape, key, twins, source, prefix));
        }

        /// <summary>The rule of an entity's metadata.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The rule.</returns>
        public static DisplayNameRule Of(EntityMetadata entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            DisplayNameMetadata rule = entity.DisplayName;
            IReadOnlyList<string?>? twins = rule.Group is MultilingualGroup group
                ? [group.Primary.Name, group.Secondary?.Name, group.Tertiary?.Name]
                : null;
            return new DisplayNameRule(entity.Key.Name, twins, rule.Source.Name, rule.Prefix?.Name);
        }

        /// <summary>The columns a caller selects to render the entity's display names under a shape.</summary>
        /// <param name="shape">The tenant's content languages.</param>
        /// <param name="navigationPath">The navigation that reaches the entity, or null for the root.</param>
        /// <returns>The column paths.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The shape is not one.</exception>
        public IReadOnlyList<string> Columns(MultilingualShape shape, string? navigationPath)
        {
            Layout layout = _layouts.GetValueOrDefault(shape) ?? throw new ArgumentOutOfRangeException(nameof(shape), shape, "Not a multilingual shape.");
            return string.IsNullOrEmpty(navigationPath)
                ? layout.Columns
                : [.. layout.Columns.Select(column => $"{navigationPath}.{column}")];
        }

        /// <summary>Renders one row's display name.</summary>
        /// <param name="values">One value per column, the shape's being read off their number.</param>
        /// <param name="contentSlot">The active content slot: 1, 2 or 3.</param>
        /// <param name="culture">The culture numbers and dates are formatted in.</param>
        /// <param name="calendar">The calendar dates are rendered in.</param>
        /// <returns>The display name, free of bidirectional controls.</returns>
        /// <exception cref="ArgumentException">The values match the columns of no shape.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The slot is not 1, 2 or 3.</exception>
        public string Render(IReadOnlyList<object?> values, int contentSlot, CultureInfo culture, ICalendarSystem calendar)
        {
            ArgumentNullException.ThrowIfNull(values);
            ArgumentNullException.ThrowIfNull(culture);
            ArgumentNullException.ThrowIfNull(calendar);
            ArgumentOutOfRangeException.ThrowIfLessThan(contentSlot, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(contentSlot, 3);

            // --- The layout the values were selected under: the narrowest shape with their number of columns ---
            Layout layout = _layouts.Values.OrderBy(static candidate => candidate.Shape).FirstOrDefault(candidate => candidate.Columns.Count == values.Count)
                ?? throw new ArgumentException(
                    $"The row has {values.Count} values, but the display-name columns of every shape number {string.Join(", ", _layouts.Values.Select(static candidate => candidate.Columns.Count).Distinct())}.",
                    nameof(values));

            // --- The name: the active slot's value when non-empty, else the first slot's ---
            string? name = layout.NameIndexes.Count >= contentSlot ? Format(values[layout.NameIndexes[contentSlot - 1]], culture, calendar) : null;
            name ??= Format(values[layout.NameIndexes[0]], culture, calendar);
            string? prefix = layout.PrefixIndex is int index ? Format(values[index], culture, calendar) : null;

            // --- The text: both joined, one alone, or the id's fallback ---
            string text = (prefix, name) switch
            {
                (string both, string named) => both + DisplayNameMetadata.Separator + named,
                (null, string named) => named,
                (string only, null) => only,
                (null, null) => Fallback(values[0], culture),
            };

            return Strip(text);
        }

        /// <summary>Formats one value: a date in the calendar's numeric style, an integer ungrouped, any other value in the culture; null for an empty value.</summary>
        /// <param name="value">The value.</param>
        /// <param name="culture">The culture.</param>
        /// <param name="calendar">The calendar.</param>
        /// <returns>The text, or null when the value is null or empty text.</returns>
        private static string? Format(object? value, CultureInfo culture, ICalendarSystem calendar)
        {
            string? text = value switch
            {
                null => null,
                string plain => plain,
                DateOnly date => calendar.Format(date, culture, DateStyle.Short),
                DateTime moment => calendar.Format(DateOnly.FromDateTime(moment), culture, DateStyle.Short),

                // "D" never groups; .NET formats Latin digits whatever the culture.
                int or long or short or byte or sbyte or uint or ulong or ushort => ((IFormattable)value).ToString("D", culture),
                IFormattable formattable => formattable.ToString(null, culture),
                _ => value.ToString(),
            };

            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>The text of a row with neither a name nor a prefix: the localised fallback around its id.</summary>
        /// <param name="id">The row's id.</param>
        /// <param name="culture">The culture.</param>
        /// <returns>The text.</returns>
        private static string Fallback(object? id, CultureInfo culture)
        {
            // The id is an integer, written ungrouped like any other integer of a display name.
            string pattern = Strings.ResourceManager.GetString(FallbackKey, culture) ?? ("#" + IdPlaceholder);
            string written = id switch
            {
                null => string.Empty,
                int or long or short or byte => ((IFormattable)id).ToString("D", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => id.ToString() ?? string.Empty,
            };
            return pattern.Replace(IdPlaceholder, written, StringComparison.Ordinal);
        }

        /// <summary>Removes the bidirectional control characters from a text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The text without them.</returns>
        private static string Strip(string text)
        {
            if (!text.Any(BidiControls.Contains))
            {
                return text;
            }

            StringBuilder stripped = new(text.Length);
            foreach (char character in text)
            {
                if (!BidiControls.Contains(character))
                {
                    _ = stripped.Append(character);
                }
            }

            return stripped.ToString();
        }

        /// <summary>The columns under one shape, and where the name's slots and the prefix sit among them.</summary>
        /// <param name="Shape">The shape.</param>
        /// <param name="Columns">The columns: the key, the admitted name columns, the prefix; each once.</param>
        /// <param name="NameIndexes">The index of each admitted name slot's column, slot 1 first.</param>
        /// <param name="PrefixIndex">The prefix column's index, or null for no prefix.</param>
        private sealed record Layout(MultilingualShape Shape, IReadOnlyList<string> Columns, IReadOnlyList<int> NameIndexes, int? PrefixIndex)
        {
            /// <summary>Lays out a rule's columns under a shape.</summary>
            /// <param name="shape">The shape.</param>
            /// <param name="key">The key's name.</param>
            /// <param name="twins">The group's twins, or null for a scalar source.</param>
            /// <param name="source">The scalar source's name.</param>
            /// <param name="prefix">The prefix's name, or null.</param>
            /// <returns>The layout.</returns>
            public static Layout Of(MultilingualShape shape, string key, IReadOnlyList<string?>? twins, string? source, string? prefix)
            {
                List<string> columns = [key];
                int IndexOf(string column)
                {
                    int found = columns.FindIndex(existing => string.Equals(existing, column, StringComparison.Ordinal));
                    if (found >= 0)
                    {
                        return found;
                    }

                    columns.Add(column);
                    return columns.Count - 1;
                }

                // A group's twins up to the shape's slot count, stopping at the first the group lacks.
                List<int> names = [];
                if (twins is null)
                {
                    names.Add(IndexOf(source!));
                }
                else
                {
                    for (int slot = 0; slot < Math.Min(twins.Count, (int)shape) && twins[slot] is string twin; slot++)
                    {
                        names.Add(IndexOf(twin));
                    }
                }

                int? prefixIndex = prefix is null ? null : IndexOf(prefix);
                return new Layout(shape, columns, names, prefixIndex);
            }
        }
    }
}
