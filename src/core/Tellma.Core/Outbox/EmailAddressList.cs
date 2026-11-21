// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §2.1 and Decisions record 12: an outgoing email's addresses are stored as RFC 5322
    // address lists, readable in a grid and filterable with contains. The outbox formats them when it
    // enqueues and the sending job parses them back, so the two are exact inverses over every address
    // the outbox accepts; anything that would not survive the round trip, or could break a header,
    // is refused before it is stored.
    /// <summary>
    ///     Formats addresses as the RFC 5322 text an outgoing email's row stores, and parses that text
    ///     back: <c>"Name" &lt;address&gt;</c> for an address with a display name, the bare address
    ///     otherwise, joined by <c>", "</c>.
    /// </summary>
    /// <remarks>
    ///     A display name is always written as a quoted string, its <c>"</c> and <c>\</c> escaped with
    ///     <c>\</c>. An address without a name is written bare unless it holds a character a list's
    ///     syntax uses (a comma, a quote, a parenthesis, a semicolon, a colon or a backslash), in which
    ///     case it is written in angle brackets.
    /// </remarks>
    internal static class EmailAddressList
    {
        /// <summary>The separator between a list's mailboxes.</summary>
        private const string Separator = ", ";

        /// <summary>The characters that make a bare address ambiguous in a list, so it is written in angle brackets.</summary>
        private static readonly SearchValues<char> Specials = SearchValues.Create(",\"();:\\");

        /// <summary>
        ///     Whether an address can be stored and sent: a non-blank address with no white space, no
        ///     control character and no angle bracket, and a display name, when it has one, with no
        ///     control character. Such an address formats and parses back unchanged.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>True when it is well formed.</returns>
        public static bool IsWellFormed(EmailAddress? address)
        {
            if (address is null || string.IsNullOrWhiteSpace(address.Address))
            {
                return false;
            }

            // --- The address: no white space, control character or angle bracket anywhere ---
            foreach (char c in address.Address)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>')
                {
                    return false;
                }
            }

            // --- The display name: no control character, so no header can be injected through it ---
            return address.DisplayName is not string name || !name.Any(char.IsControl);
        }

        /// <summary>Formats one mailbox.</summary>
        /// <param name="address">A well-formed address.</param>
        /// <returns>The mailbox's text.</returns>
        public static string Format(EmailAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            StringBuilder builder = new();
            Append(builder, address);
            return builder.ToString();
        }

        /// <summary>Formats a list of mailboxes; null when the list is empty.</summary>
        /// <param name="addresses">Well-formed addresses.</param>
        /// <returns>The list's text, or null for an empty list.</returns>
        public static string? Format(IReadOnlyList<EmailAddress> addresses)
        {
            ArgumentNullException.ThrowIfNull(addresses);
            if (addresses.Count == 0)
            {
                return null;
            }

            StringBuilder builder = new();
            for (int index = 0; index < addresses.Count; index++)
            {
                if (index > 0)
                {
                    _ = builder.Append(Separator);
                }

                Append(builder, addresses[index]);
            }

            return builder.ToString();
        }

        /// <summary>Parses a list of mailboxes, as <see cref="Format(IReadOnlyList{EmailAddress})" /> writes it.</summary>
        /// <remarks>
        ///     Also reads an unquoted display name before an angle-bracketed address
        ///     (<c>Name &lt;address&gt;</c>), and white space around every token. A blank or missing
        ///     display name parses as none.
        /// </remarks>
        /// <param name="text">The list's text; null or blank for an empty list.</param>
        /// <returns>The mailboxes, in order.</returns>
        /// <exception cref="FormatException">The text is not an address list.</exception>
        public static IReadOnlyList<EmailAddress> Parse(string? text)
        {
            List<EmailAddress> addresses = [];
            if (string.IsNullOrWhiteSpace(text))
            {
                return addresses;
            }

            int position = 0;
            while (true)
            {
                // --- One mailbox, then a separator or the end ---
                addresses.Add(ParseMailbox(text, ref position));
                SkipWhiteSpace(text, ref position);
                if (position == text.Length)
                {
                    return addresses;
                }

                if (text[position] != ',')
                {
                    throw Malformed(text, position);
                }

                position++;
            }
        }

        /// <summary>Parses one mailbox: <c>"Name" &lt;address&gt;</c>, <c>Name &lt;address&gt;</c>, <c>&lt;address&gt;</c> or a bare address.</summary>
        /// <param name="text">The list's text.</param>
        /// <param name="position">Where the mailbox may start, after leading white space; moved past it.</param>
        /// <returns>The mailbox.</returns>
        /// <exception cref="FormatException">No mailbox starts there.</exception>
        private static EmailAddress ParseMailbox(string text, ref int position)
        {
            SkipWhiteSpace(text, ref position);
            if (position == text.Length)
            {
                throw Malformed(text, position);
            }

            // --- A quoted display name, which an angle-bracketed address follows ---
            if (text[position] == '"')
            {
                string name = ReadQuoted(text, ref position);
                SkipWhiteSpace(text, ref position);
                return position < text.Length && text[position] == '<'
                    ? new EmailAddress(ReadAngled(text, ref position), NameOrNull(name))
                    : throw Malformed(text, position);
            }

            // --- An angle-bracketed address without a name ---
            if (text[position] == '<')
            {
                return new EmailAddress(ReadAngled(text, ref position));
            }

            // --- A bare address, or an unquoted name before an angle-bracketed address ---
            int start = position;
            while (position < text.Length && text[position] is not (',' or '<'))
            {
                position++;
            }

            string token = text[start..position].Trim();
            return position < text.Length && text[position] == '<'
                ? new EmailAddress(ReadAngled(text, ref position), NameOrNull(token))
                : token.Length > 0 ? new EmailAddress(token) : throw Malformed(text, start);
        }

        /// <summary>Reads a quoted string, unescaping each <c>\</c>-escaped character.</summary>
        /// <param name="text">The list's text.</param>
        /// <param name="position">The opening quote's position; moved past the closing quote.</param>
        /// <returns>The string's content.</returns>
        /// <exception cref="FormatException">The string is not closed.</exception>
        private static string ReadQuoted(string text, ref int position)
        {
            int start = position;
            StringBuilder content = new();
            position++;
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"')
                {
                    return content.ToString();
                }

                if (c == '\\')
                {
                    if (position == text.Length)
                    {
                        break;
                    }

                    c = text[position++];
                }

                _ = content.Append(c);
            }

            throw Malformed(text, start);
        }

        /// <summary>Reads an angle-bracketed address.</summary>
        /// <param name="text">The list's text.</param>
        /// <param name="position">The opening bracket's position; moved past the closing one.</param>
        /// <returns>The address, trimmed.</returns>
        /// <exception cref="FormatException">The brackets are not closed, or hold nothing.</exception>
        private static string ReadAngled(string text, ref int position)
        {
            int start = position;
            int close = text.IndexOf('>', position + 1);
            if (close < 0)
            {
                throw Malformed(text, start);
            }

            string address = text[(position + 1)..close].Trim();
            position = close + 1;
            return address.Length > 0 ? address : throw Malformed(text, start);
        }

        /// <summary>Appends one mailbox to a list's text.</summary>
        /// <param name="builder">The list's text so far.</param>
        /// <param name="address">The address.</param>
        private static void Append(StringBuilder builder, EmailAddress address)
        {
            // --- With a name: the quoted, escaped name, then the address in angle brackets ---
            if (!string.IsNullOrWhiteSpace(address.DisplayName))
            {
                _ = builder.Append('"');
                foreach (char c in address.DisplayName)
                {
                    if (c is '"' or '\\')
                    {
                        _ = builder.Append('\\');
                    }

                    _ = builder.Append(c);
                }

                _ = builder.Append("\" <").Append(address.Address).Append('>');
                return;
            }

            // --- Without one: bare, unless a list's syntax would misread it ---
            _ = address.Address.AsSpan().ContainsAny(Specials)
                ? builder.Append('<').Append(address.Address).Append('>')
                : builder.Append(address.Address);
        }

        /// <summary>Skips white space.</summary>
        /// <param name="text">The text.</param>
        /// <param name="position">The position; moved past any white space.</param>
        private static void SkipWhiteSpace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        /// <summary>A display name, or null when it is blank.</summary>
        /// <param name="name">The name as read.</param>
        /// <returns>The name, or null.</returns>
        private static string? NameOrNull(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>The failure of a parse at a position.</summary>
        /// <param name="text">The text.</param>
        /// <param name="position">Where the parse failed.</param>
        /// <returns>The exception.</returns>
        private static FormatException Malformed(string text, int position)
        {
            return new FormatException(string.Create(CultureInfo.InvariantCulture, $"The text is not an address list: it is malformed at position {position} of {text.Length}."));
        }
    }
}
