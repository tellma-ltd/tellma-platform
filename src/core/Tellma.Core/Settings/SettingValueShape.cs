// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Settings
{
    /// <summary>
    ///     What a setting key's value type is among the admitted forms: a scalar, an enum, their
    ///     nullable forms, and an <see cref="ImmutableArray{T}" /> of a scalar or enum.
    /// </summary>
    /// <param name="ValueType">The scalar kind of the value, or of each element of an array.</param>
    /// <param name="IsArray">Whether the value is an array.</param>
    /// <param name="ScalarType">The scalar or enum type: the value's type, or the array's element type, without nullability.</param>
    internal readonly record struct SettingValueShape(SettingValueType ValueType, bool IsArray, Type ScalarType)
    {
        /// <summary>The scalar types a key may hold without a serialization contract of its own, and their kinds.</summary>
        private static readonly (Type Type, SettingValueType Kind)[] Scalars =
        [
            (typeof(bool), SettingValueType.Boolean),
            (typeof(int), SettingValueType.Int32),
            (typeof(long), SettingValueType.Int64),
            (typeof(decimal), SettingValueType.Decimal),
            (typeof(string), SettingValueType.String),
            (typeof(DateOnly), SettingValueType.Date),
            (typeof(TimeOnly), SettingValueType.Time),
            (typeof(Guid), SettingValueType.Guid),
        ];

        /// <summary>
        ///     Reads a value type as one of the admitted forms: <c>T</c>, <c>T?</c>,
        ///     <c>ImmutableArray&lt;T&gt;</c> and <c>ImmutableArray&lt;T&gt;?</c>, where <c>T</c> is a
        ///     scalar of <see cref="SettingValueType" /> or an enum. An array of nullable elements is
        ///     not admitted.
        /// </summary>
        /// <param name="type">The key's value type.</param>
        /// <param name="shape">The form, when admitted.</param>
        /// <returns>True when the type is one of the admitted forms.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="type" /> is null.</exception>
        public static bool TryRead(Type type, out SettingValueShape shape)
        {
            ArgumentNullException.ThrowIfNull(type);
            shape = default;

            // --- The nullable form of a value type, then the array form ---
            Type value = Nullable.GetUnderlyingType(type) ?? type;
            bool isArray = false;
            if (value.IsGenericType && value.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
            {
                isArray = true;
                value = value.GetGenericArguments()[0];
                if (Nullable.GetUnderlyingType(value) is not null)
                {
                    return false;
                }
            }

            // --- The scalar or enum itself ---
            if (value.IsEnum)
            {
                shape = new SettingValueShape(SettingValueType.Enum, isArray, value);
                return true;
            }

            foreach ((Type scalar, SettingValueType kind) in Scalars)
            {
                if (value == scalar)
                {
                    shape = new SettingValueShape(kind, isArray, value);
                    return true;
                }
            }

            return false;
        }
    }
}
