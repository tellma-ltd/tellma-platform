// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Globalization;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     How a CLR value from a raw statement's hole binds as a scalar parameter, and when two such
    ///     values are the same value.
    /// </summary>
    internal static class ScalarValues
    {
        /// <summary>The size every string parameter up to it takes, so lengths share one plan.</summary>
        public const int StringSize = 4000;

        /// <summary>The size every binary parameter up to it takes, so lengths share one plan.</summary>
        public const int BinarySize = 8000;

        /// <summary>Whether a value binds as a scalar: null, or a type with a fixed SQL mapping.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it does.</returns>
        public static bool IsScalar(object? value)
        {
            return value is null or string or char or bool or byte or short or int or long or decimal or double or float
                or Guid or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or byte[] or Enum;
        }

        /// <summary>Describes a scalar parameter for a value, typed from its CLR type.</summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The value; see <see cref="IsScalar" />.</param>
        /// <returns>The description.</returns>
        /// <exception cref="ArgumentException">The value has no scalar mapping.</exception>
        public static ScalarParameter Describe(string name, object? value)
        {
            return value switch
            {
                // --- Text: Unicode, at one size up to 4,000 characters, max beyond ---
                null => new ScalarParameter(name, SqlDbType.NVarChar, null, StringSize),
                string text => new ScalarParameter(name, SqlDbType.NVarChar, text, text.Length <= StringSize ? StringSize : -1),
                char character => new ScalarParameter(name, SqlDbType.NVarChar, character.ToString(), StringSize),

                // --- An enum binds by name, as the platform stores enums: non-Unicode text ---
                Enum member => new ScalarParameter(name, SqlDbType.VarChar, EnumName(member), StringSize),

                // --- Numbers keep their width ---
                bool flag => new ScalarParameter(name, SqlDbType.Bit, flag),
                byte number => new ScalarParameter(name, SqlDbType.TinyInt, number),
                short number => new ScalarParameter(name, SqlDbType.SmallInt, number),
                int number => new ScalarParameter(name, SqlDbType.Int, number),
                long number => new ScalarParameter(name, SqlDbType.BigInt, number),
                decimal number => new ScalarParameter(name, SqlDbType.Decimal, number),
                double number => new ScalarParameter(name, SqlDbType.Float, number),
                float number => new ScalarParameter(name, SqlDbType.Real, number),

                // --- Identifiers and the temporal types ---
                Guid guid => new ScalarParameter(name, SqlDbType.UniqueIdentifier, guid),
                DateTime dateTime => new ScalarParameter(name, SqlDbType.DateTime2, dateTime),
                DateTimeOffset instant => new ScalarParameter(name, SqlDbType.DateTimeOffset, instant),
                DateOnly date => new ScalarParameter(name, SqlDbType.Date, date),
                TimeOnly time => new ScalarParameter(name, SqlDbType.Time, time.ToTimeSpan()),
                TimeSpan time => new ScalarParameter(name, SqlDbType.Time, time),

                // --- Binary: at one size up to 8,000 bytes, max beyond ---
                byte[] bytes => new ScalarParameter(name, SqlDbType.VarBinary, bytes, bytes.Length <= BinarySize ? BinarySize : -1),

                _ => throw new ArgumentException(
                    $"A value of type {value.GetType().Name} cannot be bound as a scalar parameter. Bind a list as a table-valued parameter, and an object name as a SqlIdentifier.",
                    nameof(value)),
            };
        }

        /// <summary>Whether two hole values are the same value: byte arrays by content, the rest by equality.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they are.</returns>
        public static bool AreEqual(object? left, object? right)
        {
            return left is byte[] leftBytes && right is byte[] rightBytes
                ? leftBytes.AsSpan().SequenceEqual(rightBytes)
                : Equals(left, right);
        }

        /// <summary>A hash consistent with <see cref="AreEqual" />.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The hash.</returns>
        public static int HashOf(object? value)
        {
            if (value is byte[] bytes)
            {
                HashCode hash = new();
                hash.AddBytes(bytes);
                return hash.ToHashCode();
            }

            return value?.GetHashCode() ?? 0;
        }

        /// <summary>The name an enum member is stored as.</summary>
        /// <param name="member">The member.</param>
        /// <returns>Its name.</returns>
        /// <exception cref="ArgumentException">The value is no single defined member.</exception>
        private static string EnumName(Enum member)
        {
            // An undefined value or a combination of flags has no stored name.
            return Enum.IsDefined(member.GetType(), member)
                ? member.ToString()
                : throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"The value {member} is not a defined member of {member.GetType().Name}, so it has no stored name."),
                    nameof(member));
        }
    }
}
