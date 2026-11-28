// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Excel
{
    // Spec 0019 §10.3: sheet values are trimmed and NFC-normalised, and compared to each other and to
    // the database's values ignoring case, ordinally; numeric and GUID keys compare by value. Every
    // comparison an import makes between keys goes through this one normalisation: row keys and
    // unique values within a sheet, the links between sheets, the in-chunk matches and the lookups.
    /// <summary>The normal form of an import's key values, and the comparer over it.</summary>
    internal static class ImportKeys
    {
        /// <summary>The most characters a text key holds: the width of the lookup's string list.</summary>
        public const int MaxTextLength = 450;

        /// <summary>Compares normal key values: text ignoring case, ordinally; any other value by its own equality.</summary>
        public static IEqualityComparer<object> Comparer { get; } = new KeyComparer();

        /// <summary>Whether a property's values are keys a lookup can restrict on: text, an enum, an integer, a GUID or a date.</summary>
        /// <param name="clrType">The property's CLR type.</param>
        /// <returns>True when they are.</returns>
        public static bool IsRestrictable(Type clrType)
        {
            ArgumentNullException.ThrowIfNull(clrType);
            Type type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            return type == typeof(string)
                || type.IsEnum
                || type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
                || type == typeof(Guid)
                || type == typeof(DateOnly);
        }

        /// <summary>
        ///     The normal form of a key value: text trimmed and NFC-normalised, an enum as its stored
        ///     name, an integer as a <see cref="long" />, a date-time as its date; any other value as
        ///     given. Null and blank text are null.
        /// </summary>
        /// <param name="value">The value, decoded from a cell or read from the database.</param>
        /// <returns>The normal form; null for none.</returns>
        public static object? Normalize(object? value)
        {
            return value switch
            {
                null => null,
                string text => Text(text),
                Enum member => member.ToString(),
                byte tiny => (long)tiny,
                sbyte signed => (long)signed,
                short small => (long)small,
                ushort unsignedSmall => (long)unsignedSmall,
                int narrow => (long)narrow,
                uint unsignedNarrow => (long)unsignedNarrow,
                long wide => wide,
                DateTime moment => DateOnly.FromDateTime(moment),
                _ => value,
            };
        }

        /// <summary>A value as an error's argument names it: text and whole numbers as they are, anything else in its invariant form.</summary>
        /// <param name="value">The value, decoded or normal.</param>
        /// <returns>The argument; null for none.</returns>
        public static object? Shown(object? value)
        {
            return value switch
            {
                null => null,
                string text => text,
                long wide => wide,
                int narrow => (long)narrow,
                short small => (long)small,
                byte tiny => (long)tiny,
                Enum member => member.ToString(),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
                DateTimeOffset instant => instant.ToString(CellCodec.StampPattern, CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
        }

        /// <summary>Whether a whole number fits an integer key type, so it can name a row of it.</summary>
        /// <param name="clrType">The key's CLR type.</param>
        /// <param name="value">The number.</param>
        /// <returns>True when it fits; true for a type that is no integer.</returns>
        public static bool Fits(Type clrType, long value)
        {
            ArgumentNullException.ThrowIfNull(clrType);
            Type type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            return (type != typeof(int) || value is >= int.MinValue and <= int.MaxValue)
                && (type != typeof(short) || value is >= short.MinValue and <= short.MaxValue)
                && (type != typeof(byte) || value is >= byte.MinValue and <= byte.MaxValue);
        }

        /// <summary>A text key trimmed and NFC-normalised; null when nothing remains.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The normal form.</returns>
        private static string? Text(string text)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            // Text that is not well-formed UTF-16 (a lone surrogate) has no normal form, and compares as written.
            try
            {
                return trimmed.IsNormalized(NormalizationForm.FormC) ? trimmed : trimmed.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return trimmed;
            }
        }

        /// <summary>Compares normal key values.</summary>
        private sealed class KeyComparer : IEqualityComparer<object>
        {
            /// <inheritdoc />
            public new bool Equals(object? x, object? y)
            {
                return x is string left && y is string right
                    ? string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                    : object.Equals(x, y);
            }

            /// <inheritdoc />
            public int GetHashCode(object obj)
            {
                ArgumentNullException.ThrowIfNull(obj);
                return obj is string text ? StringComparer.OrdinalIgnoreCase.GetHashCode(text) : obj.GetHashCode();
            }
        }
    }
}
