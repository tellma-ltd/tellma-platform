// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Queryex.Testing.Client
{
    /// <summary>A JSON value, written on one line, with its members in the order given.</summary>
    /// <remarks>
    ///     Written by hand rather than through a serializer so that the client's corpus files are
    ///     byte-for-byte the same on every host, keep their members in the order the client's types
    ///     declare them, and carry every UTF-16 code unit of a text exactly — a lone surrogate
    ///     included, which a serializer would replace or refuse. Invisible and format characters are
    ///     escaped, so a reviewer of the file sees them.
    /// </remarks>
    public sealed class ClientJson
    {
        /// <summary>The value's JSON text.</summary>
        private readonly string _text;

        /// <summary>Initializes a value from its JSON text.</summary>
        /// <param name="text">The JSON text.</param>
        private ClientJson(string text)
        {
            _text = text;
        }

        /// <summary>The JSON null.</summary>
        public static ClientJson Null { get; } = new("null");

        /// <summary>Converts a string to a JSON string.</summary>
        /// <param name="value">The string.</param>
        public static implicit operator ClientJson(string value)
        {
            return String(value);
        }

        /// <summary>Converts a whole number to a JSON number.</summary>
        /// <param name="value">The number.</param>
        public static implicit operator ClientJson(int value)
        {
            return Number(value);
        }

        /// <summary>A JSON string.</summary>
        /// <param name="value">The string.</param>
        /// <returns>The value.</returns>
        public static ClientJson String(string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            StringBuilder builder = new(value.Length + 2);
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;

                    case '\\':
                        builder.Append("\\\\");
                        break;

                    case '\n':
                        builder.Append("\\n");
                        break;

                    case '\r':
                        builder.Append("\\r");
                        break;

                    case '\t':
                        builder.Append("\\t");
                        break;

                    default:
                        // Control, format, separator-but-not-space and surrogate code units are
                        // escaped; everything a reader can see is written as itself.
                        if (NeedsEscape(character))
                        {
                            builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
            return new ClientJson(builder.ToString());
        }

        /// <summary>A JSON number.</summary>
        /// <param name="value">The number.</param>
        /// <returns>The value.</returns>
        public static ClientJson Number(int value)
        {
            return new ClientJson(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>A JSON boolean.</summary>
        /// <param name="value">The boolean.</param>
        /// <returns>The value.</returns>
        public static ClientJson Boolean(bool value)
        {
            return new ClientJson(value ? "true" : "false");
        }

        /// <summary>A JSON array.</summary>
        /// <param name="items">The items, in order.</param>
        /// <returns>The value.</returns>
        public static ClientJson Array(IEnumerable<ClientJson> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            return new ClientJson("[" + string.Join(", ", items.Select(static item => item._text)) + "]");
        }

        /// <summary>A JSON object; a member whose value is null is left out.</summary>
        /// <param name="members">The members, in order.</param>
        /// <returns>The value.</returns>
        public static ClientJson Object(params (string Name, ClientJson? Value)[] members)
        {
            ArgumentNullException.ThrowIfNull(members);
            IEnumerable<string> written = members
                .Where(static member => member.Value is not null)
                .Select(static member => String(member.Name)._text + ": " + member.Value!._text);

            return new ClientJson("{" + string.Join(", ", written) + "}");
        }

        /// <summary>The value's JSON text.</summary>
        /// <returns>The JSON text, on one line.</returns>
        public override string ToString()
        {
            return _text;
        }

        /// <summary>Whether a code unit is written as an escape rather than as itself.</summary>
        /// <param name="character">The code unit.</param>
        /// <returns>True when it is escaped.</returns>
        private static bool NeedsEscape(char character)
        {
            // A surrogate is escaped whether or not it is paired, since a lone one is not text; a
            // space other than the plain one is escaped so it cannot pass for one.
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            return char.IsSurrogate(character)
                || (category == UnicodeCategory.SpaceSeparator && character != ' ')
                || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator or UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
        }
    }
}
