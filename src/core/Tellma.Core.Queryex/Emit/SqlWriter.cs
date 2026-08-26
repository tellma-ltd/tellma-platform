// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Queryex.Emit
{
    /// <summary>
    ///     Collects the SQL text of one compilation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Line endings are always the single-character form and lines never end in blank space,
    ///         so the same query produces the same bytes on every machine and a stored snapshot of it
    ///         is a diff of the query rather than of the platform it was produced on.
    ///     </para>
    ///     <para>
    ///         No value derived from a user ever passes through here as text. Numbers written into
    ///         SQL are engine-authored constants, and everything else the caller supplied travels as
    ///         a bound parameter — which is what makes formatting culture-independent by
    ///         construction rather than by review.
    ///     </para>
    /// </remarks>
    internal sealed class SqlWriter
    {
        /// <summary>The text so far.</summary>
        private readonly StringBuilder _builder = new();

        /// <summary>Appends engine-authored SQL text.</summary>
        /// <param name="text">The text.</param>
        internal void Write(string text)
        {
            _builder.Append(text);
        }

        /// <summary>Appends one character of engine-authored SQL text.</summary>
        /// <param name="value">The character.</param>
        internal void Write(char value)
        {
            _builder.Append(value);
        }

        /// <summary>Appends a whole number the engine itself decided on.</summary>
        /// <param name="value">The number.</param>
        internal void Write(int value)
        {
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Ends the current line.</summary>
        internal void Line()
        {
            _builder.Append('\n');
        }

        /// <summary>Appends a host-authored name, quoted.</summary>
        /// <param name="name">The name.</param>
        /// <remarks>
        ///     One of only two places host text reaches emitted SQL. The closing bracket is doubled,
        ///     which is the backend's own escape and leaves no way for a name to end its own quoting.
        /// </remarks>
        internal void Quoted(string name)
        {
            _builder.Append('[');
            foreach (char character in name)
            {
                if (character == ']')
                {
                    _builder.Append(']');
                }

                _builder.Append(character);
            }

            _builder.Append(']');
        }

        /// <summary>Appends a type name.</summary>
        /// <param name="type">The type.</param>
        internal void Type(QueryexStoreType type)
        {
            switch (type.Family)
            {
                case QueryexStoreFamily.QxBit:
                    Write("bit");
                    return;

                case QueryexStoreFamily.QxTinyInt:
                    Write("tinyint");
                    return;

                case QueryexStoreFamily.QxSmallInt:
                    Write("smallint");
                    return;

                case QueryexStoreFamily.QxInt:
                    Write("int");
                    return;

                case QueryexStoreFamily.QxBigInt:
                    Write("bigint");
                    return;

                case QueryexStoreFamily.QxDecimal:

                    // Written out even where the type carries no facets of its own: a conversion has
                    // to name a concrete type, and the widest form with the promised number of
                    // fractional digits is the one that rounds nothing the language allows.
                    Write("decimal(");
                    Write(type.Size ?? SqlTypes.WidePrecision);
                    Write(", ");
                    Write(type.Scale ?? 6);
                    Write(')');
                    return;

                case QueryexStoreFamily.QxChar:
                    Sized("char", type.Size, SqlTypes.NarrowBound);
                    return;

                case QueryexStoreFamily.QxVarChar:
                    Sized("varchar", type.Size, SqlTypes.NarrowBound);
                    return;

                case QueryexStoreFamily.QxNChar:
                    Sized("nchar", type.Size, SqlTypes.UnicodeBound);
                    return;

                case QueryexStoreFamily.QxNVarChar:
                    Sized("nvarchar", type.Size, SqlTypes.UnicodeBound);
                    return;

                case QueryexStoreFamily.QxUniqueIdentifier:
                    Write("uniqueidentifier");
                    return;

                case QueryexStoreFamily.QxDate:
                    Write("date");
                    return;

                case QueryexStoreFamily.QxDateTime:
                    Write("datetime");
                    return;

                case QueryexStoreFamily.QxDateTime2:
                    Write("datetime2(");
                    Write(type.Size ?? 7);
                    Write(')');
                    return;

                case QueryexStoreFamily.QxDateTimeOffset:
                    Write("datetimeoffset(");
                    Write(type.Size ?? 7);
                    Write(')');
                    return;

                case QueryexStoreFamily.QxHierarchyId:
                    Write("hierarchyid");
                    return;

                case QueryexStoreFamily.QxGeography:
                default:
                    Write("geography");
                    return;
            }
        }

        /// <summary>Returns the text collected so far.</summary>
        /// <returns>The SQL.</returns>
        public override string ToString()
        {
            return _builder.ToString();
        }

        /// <summary>Appends a character type with its width.</summary>
        /// <param name="name">The type name.</param>
        /// <param name="size">The width, or null for the unbounded form.</param>
        /// <param name="bound">The widest the bounded form may be.</param>
        private void Sized(string name, int? size, int bound)
        {
            Write(name);
            Write('(');
            if (size is int width && width <= bound)
            {
                Write(width);
            }
            else
            {
                Write("max");
            }

            Write(')');
        }
    }
}
