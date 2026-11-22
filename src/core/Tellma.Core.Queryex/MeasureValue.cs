// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using System.Globalization;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One measure's value over one cell, typed and unboxed.
    /// </summary>
    /// <remarks>
    ///     Exactly one member holds the value, the one <see cref="Type" /> names; reading another, or
    ///     reading any of them when <see cref="IsNull" /> is true, is a mistake the accessor reports
    ///     rather than a value it makes up.
    /// </remarks>
    public readonly struct MeasureValue : IEquatable<MeasureValue>
    {
        /// <summary>The number, for a numeric value.</summary>
        private readonly SqlDecimal _number;

        /// <summary>The truth value, for a truth value.</summary>
        private readonly bool _boolean;

        /// <summary>The date, for a date.</summary>
        private readonly DateOnly _date;

        /// <summary>The date and time, for a date and time.</summary>
        private readonly DateTime _dateTime;

        /// <summary>The instant, for an instant.</summary>
        private readonly DateTimeOffset _dateTimeOffset;

        /// <summary>The text, for text and for a hierarchy position.</summary>
        private readonly string? _text;

        /// <summary>Initializes a value.</summary>
        /// <param name="type">The value's type.</param>
        /// <param name="isNull">Whether the value is absent.</param>
        /// <param name="number">The number, for a numeric value.</param>
        /// <param name="boolean">The truth value, for a truth value.</param>
        /// <param name="date">The date, for a date.</param>
        /// <param name="dateTime">The date and time, for a date and time.</param>
        /// <param name="dateTimeOffset">The instant, for an instant.</param>
        /// <param name="text">The text, for text and for a hierarchy position.</param>
        internal MeasureValue(
            QueryexType type,
            bool isNull,
            SqlDecimal number = default,
            bool boolean = false,
            DateOnly date = default,
            DateTime dateTime = default,
            DateTimeOffset dateTimeOffset = default,
            string? text = null)
        {
            Type = type;
            IsNull = isNull;
            _number = number;
            _boolean = boolean;
            _date = date;
            _dateTime = dateTime;
            _dateTimeOffset = dateTimeOffset;
            _text = text;
        }

        /// <summary>The measure's type in the language.</summary>
        public QueryexType Type { get; }

        /// <summary>Whether the measure is absent for the cell.</summary>
        public bool IsNull { get; }

        /// <summary>
        ///     The number, at the precision and scale <see cref="MeasureEvaluator.ResultTypes" />
        ///     reports for the measure.
        /// </summary>
        /// <exception cref="InvalidOperationException">The value is absent or not a number.</exception>
        public SqlDecimal Number => Present(QueryexType.QxNumeric) ? _number : default;

        /// <summary>The truth value.</summary>
        /// <exception cref="InvalidOperationException">The value is absent or not a truth value.</exception>
        public bool Boolean => Present(QueryexType.QxBool) && _boolean;

        /// <summary>The date.</summary>
        /// <exception cref="InvalidOperationException">The value is absent or not a date.</exception>
        public DateOnly Date => Present(QueryexType.QxDate) ? _date : default;

        /// <summary>The date and time.</summary>
        /// <exception cref="InvalidOperationException">The value is absent or not a date and time.</exception>
        public DateTime DateTime => Present(QueryexType.QxDateTime) ? _dateTime : default;

        /// <summary>The instant.</summary>
        /// <exception cref="InvalidOperationException">The value is absent or not an instant.</exception>
        public DateTimeOffset DateTimeOffset => Present(QueryexType.QxDateTimeOffset) ? _dateTimeOffset : default;

        /// <summary>The text, or for a hierarchy position its path, such as <c>/1/2/</c>.</summary>
        /// <exception cref="InvalidOperationException">The value is absent, or neither text nor a hierarchy position.</exception>
        public string? Text =>
            Present(Type == QueryexType.QxHierarchyId ? QueryexType.QxHierarchyId : QueryexType.QxString) ? _text : null;

        /// <summary>Whether two values are the same: of one type, both absent or holding equal values.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they are.</returns>
        public static bool operator ==(MeasureValue left, MeasureValue right)
        {
            return left.Equals(right);
        }

        /// <summary>Whether two values differ.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they do.</returns>
        public static bool operator !=(MeasureValue left, MeasureValue right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        /// <remarks>Numbers are equal when they are the same number at the same precision and scale.</remarks>
        public bool Equals(MeasureValue other)
        {
            return Type == other.Type && IsNull == other.IsNull && (IsNull || HoldsSameValue(other));
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is MeasureValue other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return IsNull
                ? HashCode.Combine(Type, true)
                : Type switch
                {
                    QueryexType.QxNumeric => HashCode.Combine(Type, _number.Scale, _number.ToString()),
                    QueryexType.QxBool => HashCode.Combine(Type, _boolean),
                    QueryexType.QxDate => HashCode.Combine(Type, _date),
                    QueryexType.QxDateTime => HashCode.Combine(Type, _dateTime),
                    QueryexType.QxDateTimeOffset => HashCode.Combine(Type, _dateTimeOffset.UtcTicks, _dateTimeOffset.Offset),
                    QueryexType.QxString or QueryexType.QxHierarchyId => HashCode.Combine(Type, _text is null ? 0 : StringComparer.Ordinal.GetHashCode(_text)),
                    QueryexType.QxGuid or QueryexType.QxGeography => Type.GetHashCode(),
                    _ => Type.GetHashCode(),
                };
        }

        /// <summary>The value in the language's invariant written form, or <c>null</c> when absent.</summary>
        /// <returns>The text.</returns>
        public override string ToString()
        {
            return IsNull
                ? "null"
                : Type switch
                {
                    QueryexType.QxNumeric => _number.ToString(),
                    QueryexType.QxBool => _boolean ? "true" : "false",
                    QueryexType.QxDate => _date.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture),
                    QueryexType.QxDateTime => _dateTime.ToString("O", CultureInfo.InvariantCulture),
                    QueryexType.QxDateTimeOffset => _dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                    QueryexType.QxString or QueryexType.QxHierarchyId => _text ?? string.Empty,
                    QueryexType.QxGuid or QueryexType.QxGeography => string.Empty,
                    _ => string.Empty,
                };
        }

        /// <summary>Whether another present value of the same type holds the same value.</summary>
        /// <param name="other">The other value.</param>
        /// <returns>True when it does.</returns>
        private bool HoldsSameValue(MeasureValue other)
        {
            return Type switch
            {
                QueryexType.QxNumeric => _number.Precision == other._number.Precision
                    && _number.Scale == other._number.Scale
                    && _number.Equals(other._number),
                QueryexType.QxBool => _boolean == other._boolean,
                QueryexType.QxDate => _date == other._date,
                QueryexType.QxDateTime => _dateTime == other._dateTime,
                QueryexType.QxDateTimeOffset => _dateTimeOffset.EqualsExact(other._dateTimeOffset),
                QueryexType.QxString or QueryexType.QxHierarchyId => string.Equals(_text, other._text, StringComparison.Ordinal),

                // No measure holds either; two of them are alike in having nothing to compare.
                QueryexType.QxGuid or QueryexType.QxGeography => true,
                _ => true,
            };
        }

        /// <summary>Checks that the value is present and of the type a member reads.</summary>
        /// <param name="type">The type the member reads.</param>
        /// <returns>True, always; the method throws otherwise.</returns>
        /// <exception cref="InvalidOperationException">The value is absent or of another type.</exception>
        private bool Present(QueryexType type)
        {
            return Type != type
                ? throw new InvalidOperationException(
                    "The measure's value is of type " + Type + ", not " + type + ".")
                : IsNull
                ? throw new InvalidOperationException("The measure's value is absent; check IsNull before reading it.")
                : true;
        }
    }
}
