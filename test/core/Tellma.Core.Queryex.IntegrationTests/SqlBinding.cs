// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.SqlTypes;
using System.Globalization;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     Binds a compiled query's parameters and reads its results back as language values.
    /// </summary>
    /// <remarks>
    ///     This is the host's half of the contract, written once here so that the differential run
    ///     exercises the same thing a real host would have to do: bind each slot by its declared
    ///     origin and store type, and read each column by the type the engine said it would be.
    /// </remarks>
    internal static class SqlBinding
    {
        /// <summary>The backend's own name for the zone the fixture runs in.</summary>
        internal const string ZoneName = "E. Africa Standard Time";

        /// <summary>Binds every slot of a compiled query onto a command.</summary>
        /// <param name="command">The command.</param>
        /// <param name="query">The compiled query.</param>
        /// <param name="context">What the values depend on.</param>
        internal static void Bind(SqlCommand command, CompiledQuery query, InterpreterContext context)
        {
            foreach (QueryexParameterSlot slot in query.Parameters)
            {
                SqlParameter parameter = command.Parameters.Add(slot.Name, TypeOf(slot.StoreType));
                Size(parameter, slot.StoreType);
                parameter.Value = ValueOf(slot, context) ?? DBNull.Value;
            }
        }

        /// <summary>Reads one row of a result set as language values.</summary>
        /// <param name="reader">The reader, positioned on a row.</param>
        /// <param name="columns">What the engine said the columns would be.</param>
        /// <returns>The values.</returns>
        internal static IReadOnlyList<QxValue> Read(SqlDataReader reader, IReadOnlyList<QueryexColumn> columns)
        {
            List<QxValue> values = [];
            for (int index = 0; index < columns.Count; index++)
            {
                values.Add(reader.IsDBNull(index)
                    ? QxValue.Absent(columns[index].Type)
                    : Convert(reader.GetValue(index), columns[index].Type));
            }

            return values;
        }

        /// <summary>Turns what the driver handed back into a language value.</summary>
        /// <param name="raw">What the driver handed back.</param>
        /// <param name="type">The type the engine said the column would be.</param>
        /// <returns>The value.</returns>
        /// <remarks>
        ///     Read tolerantly on purpose. One type in the language arrives as any of several on the
        ///     wire — a count is a wide integer where a year is a narrow one, and a truth value comes
        ///     back as a bit or as a number depending on the shape of the expression — and forcing
        ///     one storage type per language type would mean rounding perfectly good values.
        /// </remarks>
        private static QxValue Convert(object raw, QueryexType type)
        {
            return type switch
            {
                QueryexType.QxBool => QxValue.Flag(raw switch
                {
                    bool flag => flag,
                    _ => System.Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0,
                }),

                QueryexType.QxNumeric => QxValue.Number(raw switch
                {
                    decimal exact => new SqlDecimal(exact),
                    long wide => new SqlDecimal(wide),
                    int narrow => new SqlDecimal(narrow),
                    short shorter => new SqlDecimal(shorter),
                    byte tiny => new SqlDecimal(tiny),
                    _ => SqlDecimal.Parse(
                        System.Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "0"),
                }),
                QueryexType.QxString => QxValue.Text((string)raw),
                QueryexType.QxGuid => QxValue.Identifier((Guid)raw),
                QueryexType.QxDate => QxValue.Date(DateOnly.FromDateTime((DateTime)raw)),
                QueryexType.QxDateTime => QxValue.Moment((DateTime)raw),
                QueryexType.QxDateTimeOffset => QxValue.Instant((DateTimeOffset)raw),

                // Neither of the two remaining types has a bound representation, so nothing the
                // engine will let a caller select ever arrives here.
                QueryexType.QxHierarchyId or QueryexType.QxGeography => QxValue.Text(
                    System.Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty),
                _ => QxValue.Text(System.Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty),
            };
        }

        /// <summary>What a slot's value is at execution.</summary>
        /// <param name="slot">The slot.</param>
        /// <param name="context">What the values depend on.</param>
        /// <returns>The value, or null for absence.</returns>
        private static object? ValueOf(QueryexParameterSlot slot, InterpreterContext context)
        {
            return slot.Origin switch
            {
                QueryexParameterOrigin.Today => context.Today.ToDateTime(TimeOnly.MinValue),
                QueryexParameterOrigin.Now => context.Now,
                QueryexParameterOrigin.UserId => context.UserId,
                QueryexParameterOrigin.TimeZone => ZoneName,
                QueryexParameterOrigin.Declared => Supplied(slot, context),
                QueryexParameterOrigin.Literal => Literal(slot.Value),
                _ => Literal(slot.Value),
            };
        }

        /// <summary>The value supplied for a declared parameter.</summary>
        /// <param name="slot">The slot.</param>
        /// <param name="context">What the values depend on.</param>
        /// <returns>The value, or null for absence.</returns>
        private static object? Supplied(QueryexParameterSlot slot, InterpreterContext context)
        {
            return slot.DeclaredName is string name
                && context.Parameters.TryGetValue(name, out QxValue? value)
                && !value.IsAbsent
                    ? Literal(value.Raw)
                    : null;
        }

        /// <summary>A compile-time value in the form the driver wants.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The value, or null for absence.</returns>
        private static object? Literal(object? value)
        {
            return value switch
            {
                null => null,
                SqlDecimal number => number.Value,
                DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                _ => value,
            };
        }

        /// <summary>The driver's name for a store type.</summary>
        /// <param name="type">The store type.</param>
        /// <returns>The driver's type.</returns>
        private static SqlDbType TypeOf(QueryexStoreType type)
        {
            return type.Family switch
            {
                QueryexStoreFamily.QxBit => SqlDbType.Bit,
                QueryexStoreFamily.QxTinyInt => SqlDbType.TinyInt,
                QueryexStoreFamily.QxSmallInt => SqlDbType.SmallInt,
                QueryexStoreFamily.QxInt => SqlDbType.Int,
                QueryexStoreFamily.QxBigInt => SqlDbType.BigInt,
                QueryexStoreFamily.QxDecimal => SqlDbType.Decimal,
                QueryexStoreFamily.QxChar => SqlDbType.Char,
                QueryexStoreFamily.QxVarChar => SqlDbType.VarChar,
                QueryexStoreFamily.QxNChar => SqlDbType.NChar,
                QueryexStoreFamily.QxNVarChar => SqlDbType.NVarChar,
                QueryexStoreFamily.QxUniqueIdentifier => SqlDbType.UniqueIdentifier,
                QueryexStoreFamily.QxDate => SqlDbType.Date,
                QueryexStoreFamily.QxDateTime => SqlDbType.DateTime,
                QueryexStoreFamily.QxDateTime2 => SqlDbType.DateTime2,
                QueryexStoreFamily.QxDateTimeOffset => SqlDbType.DateTimeOffset,

                // Neither has a bound representation, and a declaration of one is refused before a
                // slot for it could ever be made.
                QueryexStoreFamily.QxHierarchyId or QueryexStoreFamily.QxGeography => SqlDbType.NVarChar,
                _ => SqlDbType.NVarChar,
            };
        }

        /// <summary>States a parameter's facets, where its family has any.</summary>
        /// <param name="parameter">The parameter.</param>
        /// <param name="type">The store type.</param>
        private static void Size(SqlParameter parameter, QueryexStoreType type)
        {
            if (type.Family == QueryexStoreFamily.QxDecimal)
            {
                if (type.Size is int precision && type.Scale is int scale)
                {
                    parameter.Precision = (byte)precision;
                    parameter.Scale = (byte)scale;
                }

                return;
            }

            if (type.Family is QueryexStoreFamily.QxChar or QueryexStoreFamily.QxVarChar
                or QueryexStoreFamily.QxNChar or QueryexStoreFamily.QxNVarChar)
            {
                parameter.Size = type.Size ?? -1;
            }
        }
    }
}
