// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     One value of the language, or the absence of one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Absence is a state of a typed value here, never a missing object. The whole point of
    ///         the second implementation is to check what the compiler concludes about absence, and
    ///         a representation in which absence and "no value at all" were the same thing could not
    ///         tell the two apart.
    ///     </para>
    ///     <para>
    ///         Numbers are carried in the type that models the backend's exact decimals rather than
    ///         the platform's, whose range is narrower than the language allows — a conforming number
    ///         is simply not representable in the obvious type.
    ///     </para>
    /// </remarks>
    public sealed class QxValue
    {
        /// <summary>Initializes a value.</summary>
        /// <param name="type">Its type.</param>
        /// <param name="raw">The value, or null for absence.</param>
        private QxValue(QueryexType type, object? raw)
        {
            Type = type;
            Raw = raw;
        }

        /// <summary>The value's type.</summary>
        public QueryexType Type { get; }

        /// <summary>The value itself, or null when it is absent.</summary>
        public object? Raw { get; }

        /// <summary>Whether there is no value.</summary>
        public bool IsAbsent => Raw is null;

        /// <summary>The absence of a value of a given type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The value.</returns>
        public static QxValue Absent(QueryexType type)
        {
            return new QxValue(type, null);
        }

        /// <summary>A number.</summary>
        /// <param name="value">The number.</param>
        /// <returns>The value.</returns>
        public static QxValue Number(SqlDecimal value)
        {
            return new QxValue(QueryexType.QxNumeric, value);
        }

        /// <summary>A whole number.</summary>
        /// <param name="value">The number.</param>
        /// <returns>The value.</returns>
        public static QxValue Number(long value)
        {
            return new QxValue(QueryexType.QxNumeric, new SqlDecimal(value));
        }

        /// <summary>Text.</summary>
        /// <param name="value">The text.</param>
        /// <returns>The value.</returns>
        public static QxValue Text(string value)
        {
            return new QxValue(QueryexType.QxString, value);
        }

        /// <summary>A truth value.</summary>
        /// <param name="value">The truth value.</param>
        /// <returns>The value.</returns>
        public static QxValue Flag(bool value)
        {
            return new QxValue(QueryexType.QxBool, value);
        }

        /// <summary>An identifier.</summary>
        /// <param name="value">The identifier.</param>
        /// <returns>The value.</returns>
        public static QxValue Identifier(Guid value)
        {
            return new QxValue(QueryexType.QxGuid, value);
        }

        /// <summary>A calendar date.</summary>
        /// <param name="value">The date.</param>
        /// <returns>The value.</returns>
        public static QxValue Date(DateOnly value)
        {
            return new QxValue(QueryexType.QxDate, value);
        }

        /// <summary>A date and time with no offset.</summary>
        /// <param name="value">The moment.</param>
        /// <returns>The value.</returns>
        public static QxValue Moment(DateTime value)
        {
            return new QxValue(QueryexType.QxDateTime, value);
        }

        /// <summary>An instant carrying its own offset.</summary>
        /// <param name="value">The instant.</param>
        /// <returns>The value.</returns>
        public static QxValue Instant(DateTimeOffset value)
        {
            return new QxValue(QueryexType.QxDateTimeOffset, value);
        }

        /// <summary>A position in a hierarchy, written as a path.</summary>
        /// <param name="value">The path, such as <c>/1/2/</c>.</param>
        /// <returns>The value.</returns>
        public static QxValue Node(string value)
        {
            return new QxValue(QueryexType.QxHierarchyId, value);
        }

        /// <summary>A spatial value, which the language only ever carries about.</summary>
        /// <param name="value">Its written form.</param>
        /// <returns>The value.</returns>
        public static QxValue Spatial(string value)
        {
            return new QxValue(QueryexType.QxGeography, value);
        }

        /// <summary>A value of a stated type, or absence when none was given.</summary>
        /// <param name="type">The type.</param>
        /// <param name="raw">The value, or null.</param>
        /// <returns>The value.</returns>
        public static QxValue Of(QueryexType type, object? raw)
        {
            return new QxValue(type, raw);
        }

        /// <summary>The number this value carries.</summary>
        public SqlDecimal AsNumber => (SqlDecimal)Raw!;

        /// <summary>The text this value carries.</summary>
        public string AsText => (string)Raw!;

        /// <summary>The truth value this value carries.</summary>
        public bool AsFlag => (bool)Raw!;

        /// <summary>The identifier this value carries.</summary>
        public Guid AsIdentifier => (Guid)Raw!;

        /// <summary>The date this value carries.</summary>
        public DateOnly AsDate => (DateOnly)Raw!;

        /// <summary>The moment this value carries.</summary>
        public DateTime AsMoment => (DateTime)Raw!;

        /// <summary>The instant this value carries.</summary>
        public DateTimeOffset AsInstant => (DateTimeOffset)Raw!;

        /// <summary>The hierarchy path this value carries.</summary>
        public string AsNode => (string)Raw!;

        /// <summary>Renders the value, for a failing comparison to name what it saw.</summary>
        /// <returns>The rendered value.</returns>
        public override string ToString()
        {
            return IsAbsent ? "absent<" + Type + ">" : Raw!.ToString() ?? string.Empty;
        }
    }
}
