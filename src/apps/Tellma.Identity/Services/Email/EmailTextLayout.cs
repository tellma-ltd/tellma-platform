// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Renders an <see cref="EmailContent" /> into the plain-text alternative of a message.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built from the same content as the HTML so the two say the same thing. That is worth
    ///         doing for its own sake — a reader whose client refuses HTML should not get a lesser
    ///         message — and it also keeps the copy in one place rather than two that drift. Filters
    ///         score a multipart message whose alternatives disagree, mildly, and a message with no
    ///         text part at all rather more; this answers both.
    ///     </para>
    ///     <para>
    ///         Paragraphs are separated by a blank line and lines end CRLF, which is what mail uses
    ///         on the wire regardless of the host it was composed on. The one structural difference
    ///         from the HTML is the action: there is no button to press, so the label introduces the
    ///         address instead of hiding it, and the note about a button that did not work is left
    ///         out as the only sentence that would be untrue here.
    ///     </para>
    /// </remarks>
    internal static class EmailTextLayout
    {
        /// <summary>Separates paragraphs; CRLF because that is what mail uses on the wire.</summary>
        private const string ParagraphBreak = "\r\n\r\n";

        /// <summary>Renders the plain-text alternative for one message.</summary>
        /// <param name="content">What the message says.</param>
        /// <returns>The body.</returns>
        internal static string Render(EmailContent content)
        {
            ArgumentNullException.ThrowIfNull(content);

            List<string> paragraphs = [content.Heading, .. content.Paragraphs];

            if (content.Code is not null)
            {
                // On a line of its own, which is what makes it selectable as one word in every
                // client and readable when a narrow window wraps everything around it.
                paragraphs.Add(content.Code);
            }

            if (content.CodeNote is not null)
            {
                paragraphs.Add(content.CodeNote);
            }

            if (content.Action is not null)
            {
                paragraphs.Add(content.Action.Label + ":\r\n" + content.Action.Url);
                paragraphs.Add(content.Action.Validity);
            }

            paragraphs.AddRange(content.Notes);
            paragraphs.Add(Footer(content));

            return string.Join(ParagraphBreak, paragraphs);
        }

        /// <summary>Builds the closing block: why the message arrived, then any legal links.</summary>
        private static string Footer(EmailContent content)
        {
            StringBuilder footer = new(content.FooterNote);

            // One per line rather than run together: a line break is the only separator that
            // cannot be mistaken for part of the address next to it.
            foreach (EmailLink link in content.FooterLinks)
            {
                footer.Append("\r\n").Append(link.Label).Append(": ").Append(link.Url);
            }

            return footer.ToString();
        }
    }
}
