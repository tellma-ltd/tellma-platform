// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Crud.Save;

namespace Tellma.Core.Excel
{
    // Spec 0019 §3.1: a cell is decoded by the type of the property its column maps to, never by the
    // cell's own format: an identity and a parent link as a whole number, the stamp as an instant, a
    // reference in the type of the target's key, any other column in its property's type with what
    // that type needs to read text. A text date column of an export is read in the calendar and the
    // culture the manifest records it was written in.
    /// <summary>What each mapped column of an import decodes into.</summary>
    /// <param name="valueSets">The value sets, whose values a value-set column is read by.</param>
    /// <param name="calendars">The calendars, by the code a manifest records.</param>
    /// <param name="cache">The cache of the choice labels.</param>
    /// <param name="context">The request's context.</param>
    /// <param name="manifest">The workbook's manifest; null when it carries none.</param>
    internal sealed class ImportDecoders(IValueSetRegistry valueSets, ICalendarRegistry calendars, HeaderIndexCache cache, ExcelContext context, ImportManifest? manifest)
    {
        /// <summary>The manifest's date encoding of a column written as text.</summary>
        private static readonly string TextEncoding = nameof(ExcelDateEncoding.Text);

        /// <summary>The value sets.</summary>
        private readonly IValueSetRegistry _valueSets = valueSets ?? throw new ArgumentNullException(nameof(valueSets));


        /// <summary>The cache of the choice labels.</summary>
        private readonly HeaderIndexCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        /// <summary>The request's context.</summary>
        private readonly ExcelContext _context = context ?? throw new ArgumentNullException(nameof(context));

        /// <summary>The calendar and culture the manifest records its text dates in, read once; null when it records none this host knows.</summary>
        private readonly (ICalendarSystem Calendar, CultureInfo? Culture)? _textDates = TextDates(manifest, calendars);

        /// <summary>The manifest's columns, by sheet and header.</summary>
        private readonly Dictionary<(string Sheet, string Header), ImportManifestColumn> _recorded = Recorded(manifest);

        /// <summary>What a mapped column decodes into.</summary>
        /// <param name="sheet">The column's sheet.</param>
        /// <param name="entity">The entity of the sheet's rows.</param>
        /// <param name="column">The column, mapped to a path.</param>
        /// <param name="path">The column's path.</param>
        /// <returns>The target.</returns>
        public CellDecodeTarget For(string sheet, EntityMetadata entity, ImportMappedColumn column, ImportPath path)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(column);
            ArgumentNullException.ThrowIfNull(path);
            return path.Kind switch
            {
                // --- The identities, read as whole numbers whatever the key's width, and the stamp as an instant ---
                ImportPathKind.Id or ImportPathKind.ParentReference => new CellDecodeTarget(typeof(long)),
                ImportPathKind.Stamp => new CellDecodeTarget(typeof(DateTimeOffset)),

                // --- A reference: in the type of the target's key, with its choices when it is one ---
                ImportPathKind.Reference when path.Reference is ReferenceMetadata reference && path.Key is PropertyMetadata key
                    => ReferenceEquals(key, reference.Target.Key) || key.Name == reference.Target.Key.Name
                        ? new CellDecodeTarget(typeof(long))
                        : Scalar(sheet, reference.Target, key, column, preserveWhitespace: false),

                // --- Any other column: its property's type ---
                ImportPathKind.Property => Scalar(sheet, entity, path.Property, column, RowRules.PreservesWhitespace(entity, path.Property)),
                ImportPathKind.Reference or ImportPathKind.Navigation => throw new ArgumentException($"The path {path.Text} is no column the import reads.", nameof(path)),
                _ => throw new ArgumentException($"The path {path.Text} is no column the import reads.", nameof(path)),
            };
        }

        /// <summary>The target of a scalar: its type, scale, choices and their labels, white space, and a text date's calendar.</summary>
        /// <param name="sheet">The column's sheet.</param>
        /// <param name="entity">The entity declaring the property.</param>
        /// <param name="property">The property.</param>
        /// <param name="column">The column.</param>
        /// <param name="preserveWhitespace">Whether text keeps its leading and trailing white space.</param>
        /// <returns>The target.</returns>
        private CellDecodeTarget Scalar(string sheet, EntityMetadata entity, PropertyMetadata property, ImportMappedColumn column, bool preserveWhitespace)
        {
            // --- The choices: an enum's names, or a value set's values ---
            Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            IReadOnlyList<string>? choices = type.IsEnum
                ? Enum.GetNames(type)
                : property.ValueSet is string set && _valueSets.Find(set) is ValueSetDescriptor descriptor
                    ? [.. descriptor.Values.Select(static entry => entry.Value)]
                    : null;

            // --- A text date column: the calendar and culture the manifest records it was written in ---
            bool textDate = _textDates is not null
                && _recorded.TryGetValue((sheet, column.Header), out ImportManifestColumn? recorded)
                && string.Equals(recorded.DateEncoding, TextEncoding, StringComparison.Ordinal);

            return new CellDecodeTarget(property.ClrType)
            {
                Scale = property.Scale,
                Choices = type.IsEnum ? null : choices,
                ChoiceLabels = choices is null ? null : _cache.ChoiceLabels(entity, property, choices, _context),
                PreserveWhitespace = preserveWhitespace,
                TextCalendar = textDate ? _textDates!.Value.Calendar : null,
                TextCulture = textDate ? _textDates!.Value.Culture : null,
            };
        }

        /// <summary>The calendar and culture a manifest records, when this host knows the calendar.</summary>
        /// <param name="manifest">The manifest; null for none.</param>
        /// <param name="calendars">The calendars.</param>
        /// <returns>The calendar and the culture, which is null when the manifest names none this host knows; null without a known calendar.</returns>
        private static (ICalendarSystem Calendar, CultureInfo? Culture)? TextDates(ImportManifest? manifest, ICalendarRegistry calendars)
        {
            ArgumentNullException.ThrowIfNull(calendars);
            if (manifest?.Header.GetValueOrDefault(ExcelManifest.CalendarKey) is not string code || calendars.TryGet(code) is not ICalendarSystem calendar)
            {
                return null;
            }

            // The manifest is untrusted: a culture name this host does not know reads in the request's culture.
            CultureInfo? culture = null;
            if (manifest.Header.GetValueOrDefault(ExcelManifest.CultureKey) is string name && name.Length > 0)
            {
                try
                {
                    culture = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
                }
                catch (CultureNotFoundException)
                {
                    culture = null;
                }
            }

            return (calendar, culture);
        }

        /// <summary>The manifest's columns by sheet and exact header; the first of a repeated pair wins.</summary>
        /// <param name="manifest">The manifest; null for none.</param>
        /// <returns>The columns.</returns>
        private static Dictionary<(string Sheet, string Header), ImportManifestColumn> Recorded(ImportManifest? manifest)
        {
            Dictionary<(string Sheet, string Header), ImportManifestColumn> recorded = new(SheetHeaderComparer.Instance);
            foreach (ImportManifestColumn column in manifest?.Columns ?? [])
            {
                _ = recorded.TryAdd((column.Sheet, column.Header), column);
            }

            return recorded;
        }

        /// <summary>Compares a sheet and a header: the sheet ignoring case, as sheet names are, and the header exactly.</summary>
        private sealed class SheetHeaderComparer : IEqualityComparer<(string Sheet, string Header)>
        {
            /// <summary>The comparer.</summary>
            public static readonly SheetHeaderComparer Instance = new();

            /// <inheritdoc />
            public bool Equals((string Sheet, string Header) x, (string Sheet, string Header) y)
            {
                return string.Equals(x.Header, y.Header, StringComparison.Ordinal) && string.Equals(x.Sheet, y.Sheet, StringComparison.OrdinalIgnoreCase);
            }

            /// <inheritdoc />
            public int GetHashCode((string Sheet, string Header) obj)
            {
                return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sheet), StringComparer.Ordinal.GetHashCode(obj.Header));
            }
        }
    }
}
