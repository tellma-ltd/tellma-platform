// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient.Server;
using System.Data;
using System.Data.SqlTypes;
using System.Globalization;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.Data.TableTypes
{
    /// <summary>
    ///     Turns a table-type column's store type and facets into the <see cref="SqlMetaData" /> a
    ///     table-valued parameter declares for it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Character columns declare a fixed locale, the one of the platform collation
    ///         (<c>Latin1_General</c>, LCID 1033), rather than the client thread's culture, which the
    ///         driver would otherwise use to encode <c>char</c> and <c>varchar</c> values: a host running
    ///         under another culture would send different bytes for the same text. A non-unicode column
    ///         with a collation outside the Latin1 family cannot be encoded correctly this way and is
    ///         refused, as is a UTF-8 collation; such a column is declared <c>nvarchar</c> instead.
    ///     </para>
    ///     <para>
    ///         Store types the platform never binds (<c>hierarchyid</c>, <c>xml</c>, <c>sql_variant</c>,
    ///         spatial types) are refused with the column named.
    ///     </para>
    /// </remarks>
    internal static class StoreTypeMetadata
    {
        /// <summary>The locale of the platform collation, <c>Latin1_General_100_CI_AS</c>.</summary>
        public const long PlatformLocale = 1033;

        /// <summary>The comparison options of a case-insensitive, accent-sensitive collation, the platform's.</summary>
        public const SqlCompareOptions PlatformCompareOptions =
            SqlCompareOptions.IgnoreCase | SqlCompareOptions.IgnoreKanaType | SqlCompareOptions.IgnoreWidth;

        /// <summary>The fractional-second digits of a temporal type that declares none.</summary>
        private const byte DefaultTimeScale = 7;

        /// <summary>The metadata of one column.</summary>
        /// <param name="definition">The type the column belongs to, for messages.</param>
        /// <param name="column">The column.</param>
        /// <returns>The metadata.</returns>
        /// <exception cref="NotSupportedException">The column's store type cannot be bound.</exception>
        public static SqlMetaData For(TableTypeDefinition definition, TableTypeColumnDefinition column)
        {
            (string baseType, int? first, int? second, bool max) = Parse(column.StoreType);
            string name = column.Name;

            return baseType switch
            {
                // --- Exact numbers and flags ---
                "bit" => new SqlMetaData(name, SqlDbType.Bit),
                "tinyint" => new SqlMetaData(name, SqlDbType.TinyInt),
                "smallint" => new SqlMetaData(name, SqlDbType.SmallInt),
                "int" => new SqlMetaData(name, SqlDbType.Int),
                "bigint" => new SqlMetaData(name, SqlDbType.BigInt),
                "decimal" or "numeric" => new SqlMetaData(
                    name,
                    SqlDbType.Decimal,
                    checked((byte)(column.Precision ?? first ?? 18)),
                    checked((byte)(column.Scale ?? second ?? 0))),
                "money" => new SqlMetaData(name, SqlDbType.Money),
                "smallmoney" => new SqlMetaData(name, SqlDbType.SmallMoney),

                // --- Approximate numbers ---
                "float" => new SqlMetaData(name, SqlDbType.Float),
                "real" => new SqlMetaData(name, SqlDbType.Real),

                // --- Dates and times ---
                "date" => new SqlMetaData(name, SqlDbType.Date),
                "datetime" => new SqlMetaData(name, SqlDbType.DateTime),
                "smalldatetime" => new SqlMetaData(name, SqlDbType.SmallDateTime),
                "datetime2" => new SqlMetaData(name, SqlDbType.DateTime2, 0, Scale(column, first)),
                "datetimeoffset" => new SqlMetaData(name, SqlDbType.DateTimeOffset, 0, Scale(column, first)),
                "time" => new SqlMetaData(name, SqlDbType.Time, 0, Scale(column, first)),

                // --- Identifiers and bytes ---
                "uniqueidentifier" => new SqlMetaData(name, SqlDbType.UniqueIdentifier),
                "binary" => new SqlMetaData(name, SqlDbType.Binary, Length(column, first, max, definition)),
                "varbinary" => new SqlMetaData(name, SqlDbType.VarBinary, Length(column, first, max, definition)),

                // --- Text ---
                "nvarchar" => Text(name, SqlDbType.NVarChar, Length(column, first, max, definition)),
                "nchar" => Text(name, SqlDbType.NChar, Length(column, first, max, definition)),
                "varchar" => NonUnicodeText(definition, column, SqlDbType.VarChar, Length(column, first, max, definition)),
                "char" => NonUnicodeText(definition, column, SqlDbType.Char, Length(column, first, max, definition)),

                _ => throw new NotSupportedException(
                    $"Column '{name}' of table type {definition.DisplayName} has the store type '{column.StoreType}', which a table-valued parameter cannot bind."),
            };
        }

        /// <summary>Splits a store type such as <c>decimal(19,4)</c> or <c>nvarchar(max)</c> into its base name and facets.</summary>
        /// <param name="storeType">The store type.</param>
        /// <returns>The lower-case base name, the first and second numeric facets, and whether the length is <c>max</c>.</returns>
        public static (string BaseType, int? First, int? Second, bool Max) Parse(string storeType)
        {
            ArgumentNullException.ThrowIfNull(storeType);

            int open = storeType.IndexOf('(', StringComparison.Ordinal);
            string baseType = (open < 0 ? storeType : storeType[..open]).Trim().ToLowerInvariant();
            if (open < 0)
            {
                return (baseType, null, null, false);
            }

            int close = storeType.IndexOf(')', open);
            string[] facets = storeType[(open + 1)..(close < 0 ? storeType.Length : close)].Split(',', StringSplitOptions.TrimEntries);
            bool max = facets is [string only] && string.Equals(only, "max", StringComparison.OrdinalIgnoreCase);

            return (baseType, max ? null : Number(facets, 0), Number(facets, 1), max);
        }

        /// <summary>A Unicode text column's metadata, with the platform locale.</summary>
        /// <param name="name">The column name.</param>
        /// <param name="type">The text type.</param>
        /// <param name="length">The length, or <see cref="SqlMetaData.Max" />.</param>
        /// <returns>The metadata.</returns>
        private static SqlMetaData Text(string name, SqlDbType type, long length)
        {
            return new SqlMetaData(name, type, length, PlatformLocale, PlatformCompareOptions);
        }

        /// <summary>A non-unicode text column's metadata, refused for a collation the platform locale cannot encode.</summary>
        /// <param name="definition">The type, for messages.</param>
        /// <param name="column">The column.</param>
        /// <param name="type">The text type.</param>
        /// <param name="length">The length, or <see cref="SqlMetaData.Max" />.</param>
        /// <returns>The metadata.</returns>
        /// <exception cref="NotSupportedException">The column's collation uses another code page, or UTF-8.</exception>
        private static SqlMetaData NonUnicodeText(TableTypeDefinition definition, TableTypeColumnDefinition column, SqlDbType type, long length)
        {
            // A column without its own collation takes the database's, the platform collation. The
            // Latin1 family (Latin1_General_*, SQL_Latin1_General_CP1_*) shares code page 1252.
            string? collation = column.Collation;
            bool latin1 = collation is null
                || ((collation.StartsWith("Latin1_General", StringComparison.OrdinalIgnoreCase)
                        || collation.StartsWith("SQL_Latin1_General_CP1_", StringComparison.OrdinalIgnoreCase))
                    && !collation.Contains("UTF8", StringComparison.OrdinalIgnoreCase));

            return latin1
                ? Text(column.Name, type, length)
                : throw new NotSupportedException(
                    $"Column '{column.Name}' of table type {definition.DisplayName} is non-unicode text in collation '{collation}', which a table-valued parameter cannot encode correctly. Declare the column nvarchar.");
        }

        /// <summary>The length of a text or binary column: <see cref="SqlMetaData.Max" /> for <c>max</c>, else the facet.</summary>
        /// <param name="column">The column.</param>
        /// <param name="first">The parsed length facet.</param>
        /// <param name="max">Whether the store type says <c>max</c>.</param>
        /// <param name="definition">The type, for messages.</param>
        /// <returns>The length.</returns>
        /// <exception cref="NotSupportedException">The column has no length.</exception>
        private static long Length(TableTypeColumnDefinition column, int? first, bool max, TableTypeDefinition definition)
        {
            return max
                ? SqlMetaData.Max
                : column.MaxLength ?? first
                    ?? throw new NotSupportedException(
                        $"Column '{column.Name}' of table type {definition.DisplayName} has the store type '{column.StoreType}' with no length.");
        }

        /// <summary>The fractional-second digits of a temporal column: the facet, else 7.</summary>
        /// <param name="column">The column.</param>
        /// <param name="first">The parsed scale facet.</param>
        /// <returns>The scale.</returns>
        private static byte Scale(TableTypeColumnDefinition column, int? first)
        {
            return checked((byte)(column.Precision ?? column.Scale ?? first ?? DefaultTimeScale));
        }

        /// <summary>One numeric facet, or <see langword="null" />.</summary>
        /// <param name="facets">The facets.</param>
        /// <param name="index">The facet's position.</param>
        /// <returns>The value, or <see langword="null" />.</returns>
        private static int? Number(string[] facets, int index)
        {
            return index < facets.Length && int.TryParse(facets[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
        }
    }
}
