// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Client;
using Tellma.Core.Queryex;

namespace Tellma.Core.Client
{
    // The mapping is spec 0029 §2.2's: every integer width is an integer, a long also says so, a
    // DateTime is a wall-clock date-time and a DateTimeOffset an instant.
    /// <summary>The client's data type of a CLR type or a store type.</summary>
    internal static class ClientDataTypes
    {
        /// <summary>The client's data type of a scalar CLR type, a nullable of one included.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The data type, or null when the type is no scalar the client knows, such as a record or a list.</returns>
        public static ClientDataType? Of(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            Type scalar = Nullable.GetUnderlyingType(type) ?? type;
            return scalar switch
            {
                _ when scalar.IsEnum => ClientDataType.Enum,
                _ when scalar == typeof(string) => ClientDataType.String,
                _ when scalar == typeof(int) || scalar == typeof(short) || scalar == typeof(byte) || scalar == typeof(long) => ClientDataType.Integer,
                _ when scalar == typeof(decimal) => ClientDataType.Decimal,
                _ when scalar == typeof(double) || scalar == typeof(float) => ClientDataType.Float,
                _ when scalar == typeof(bool) => ClientDataType.Bool,
                _ when scalar == typeof(DateOnly) => ClientDataType.Date,
                _ when scalar == typeof(DateTime) => ClientDataType.DateTime,
                _ when scalar == typeof(DateTimeOffset) => ClientDataType.Instant,
                _ when scalar == typeof(TimeOnly) => ClientDataType.Time,
                _ when scalar == typeof(Guid) => ClientDataType.Guid,
                _ when scalar == typeof(byte[]) => ClientDataType.Binary,
                _ => null,
            };
        }

        /// <summary>Whether a CLR type is a <see cref="long" />, or a nullable of one, whose values travel as decimal text.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True for a long.</returns>
        public static bool IsLong(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return (Nullable.GetUnderlyingType(type) ?? type) == typeof(long);
        }

        /// <summary>
        ///     The element of a list type: an array, or a type that implements <see cref="IEnumerable{T}" />;
        ///     never of a string or a byte array, which are scalars.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The element type, or null for a type that is no list.</returns>
        public static Type? ElementOf(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type == typeof(string) || type == typeof(byte[]))
            {
                return null;
            }

            if (type.IsArray)
            {
                return type.GetElementType();
            }

            // The type itself when it is IEnumerable<T>, else the one it implements; a dictionary is
            // an object on the wire, never a list.
            Type? enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().FirstOrDefault(static candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            bool dictionary = type.GetInterfaces().Append(type).Any(static candidate =>
                candidate.IsGenericType
                && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
            return enumerable is null || dictionary ? null : enumerable.GetGenericArguments()[0];
        }

        /// <summary>The client's data type of a store type, for a read-only root, which has no CLR type.</summary>
        /// <param name="family">The store type's family.</param>
        /// <returns>The data type; a hierarchy node or a geography value is opaque binary.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="family" /> is not a defined family.</exception>
        public static ClientDataType Of(QueryexStoreFamily family)
        {
            return family switch
            {
                QueryexStoreFamily.QxBit => ClientDataType.Bool,
                QueryexStoreFamily.QxTinyInt or QueryexStoreFamily.QxSmallInt or QueryexStoreFamily.QxInt or QueryexStoreFamily.QxBigInt => ClientDataType.Integer,
                QueryexStoreFamily.QxDecimal => ClientDataType.Decimal,
                QueryexStoreFamily.QxChar or QueryexStoreFamily.QxVarChar or QueryexStoreFamily.QxNChar or QueryexStoreFamily.QxNVarChar => ClientDataType.String,
                QueryexStoreFamily.QxUniqueIdentifier => ClientDataType.Guid,
                QueryexStoreFamily.QxDate => ClientDataType.Date,
                QueryexStoreFamily.QxDateTime or QueryexStoreFamily.QxDateTime2 => ClientDataType.DateTime,
                QueryexStoreFamily.QxDateTimeOffset => ClientDataType.Instant,
                QueryexStoreFamily.QxHierarchyId or QueryexStoreFamily.QxGeography => ClientDataType.Binary,
                _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Not a defined store type family."),
            };
        }
    }
}
