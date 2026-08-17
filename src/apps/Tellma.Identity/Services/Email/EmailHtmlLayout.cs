// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Text;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Renders an <see cref="EmailContent" /> into the HTML alternative of a message: an ink
    ///     banner carrying the brand mark, a heading, the prose, one thing to act on, and a grey
    ///     footer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Written for mail clients rather than for browsers, which rules out most of how a page
    ///         is normally built. Layout is nested tables, because Outlook renders through Word and
    ///         has no flexbox or grid and no reliable block layout. Every visual rule is an inline
    ///         <c>style</c> attribute, because Gmail discards a document's stylesheet when the
    ///         account it is displaying is not a Gmail one — so the single <c>&lt;style&gt;</c> block
    ///         here may only hold refinements the design still reads correctly without.
    ///     </para>
    ///     <para>
    ///         Colors are literals, not custom properties, for the same reason: <c>var()</c> resolves
    ///         nowhere in Outlook or Gmail. They are the same values the design tokens emit, so the
    ///         mail matches the product; a token change has to be copied here deliberately.
    ///     </para>
    ///     <para>
    ///         Right-to-left is the document's own direction rather than a per-element override, so
    ///         the whole layout mirrors and the Latin fragments inside it — the address behind the
    ///         button, the one-time code — are isolated back to left-to-right individually.
    ///     </para>
    /// </remarks>
    internal static class EmailHtmlLayout
    {
        // The palette, copied from the emitted design tokens. Named for the token each one is.
        private const string Ink = "#001722";              // --ink-900, the banner
        private const string White = "#FEFEFE";            // --white, the card
        private const string PageBackground = "#EEF3F4";   // --grey-50, the pane behind the card
        private const string FooterBackground = "#F7FAFB"; // --grey-25, the footer
        private const string BodyText = "#283A41";         // --grey-700, prose
        private const string SecondaryText = "#56686F";    // --grey-500, small print
        private const string Primary = "#316E80";          // --teal-600, the button and links
        private const string SubtleBorder = "#E1E8EA";     // --grey-100, hairlines
        private const string CardBorder = "#CBD6D9";       // --grey-200, the card's own edge
        private const string CodeBackground = "#EAF4F7";   // --teal-50, the code panel
        private const string CodeBorder = "#CBE6EC";       // --teal-100, its edge

        // One stack for both scripts. Tahoma precedes Arial because it carries the better Arabic
        // face on Windows, where most mail is read, and neither is ever downloaded: mail clients do
        // not honour @font-face, so the brand faces the UI uses are not available here.
        private const string FontStack =
            "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Tahoma,Arial,Helvetica,sans-serif";

        // Courier New is the one monospace face present on effectively every client.
        private const string MonoStack = "Consolas,'Courier New',Courier,monospace";

        /// <summary>The card's width, in CSS pixels; the width every email design has settled on.</summary>
        private const int CardWidth = 600;

        /// <summary>Renders the HTML alternative for one message.</summary>
        /// <param name="content">What the message says.</param>
        /// <param name="subject">The subject, used as the document title.</param>
        /// <param name="productName">The brand name, used as the mark's text alternative.</param>
        /// <param name="culture">The recipient's culture, which decides the document's direction.</param>
        /// <returns>A complete HTML document.</returns>
        internal static string Render(
            EmailContent content, string subject, string productName, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(culture);

            bool rtl = culture.TextInfo.IsRightToLeft;
            string dir = rtl ? "rtl" : "ltr";
            string start = rtl ? "right" : "left";

            StringBuilder html = new(4096);

            // The document. The Office namespaces and the PixelsPerInch block are what stop Outlook
            // scaling the whole layout up on a high-DPI display.
            html.Append("<!DOCTYPE html>\r\n")
                .Append(CultureInfo.InvariantCulture, $"<html lang=\"{Encode(culture.TwoLetterISOLanguageName)}\" dir=\"{dir}\" ")
                .Append("xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\">\r\n")
                .Append("<head>\r\n")
                .Append("<meta charset=\"utf-8\" />\r\n")
                .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\r\n")
                .Append("<meta name=\"x-apple-disable-message-reformatting\" />\r\n")
                .Append("<meta name=\"color-scheme\" content=\"light\" />\r\n")
                .Append("<meta name=\"supported-color-schemes\" content=\"light\" />\r\n")
                .Append(CultureInfo.InvariantCulture, $"<title>{Encode(subject)}</title>\r\n")
                .Append("<!--[if mso]><noscript><xml><o:OfficeDocumentSettings>")
                .Append("<o:PixelsPerInch>96</o:PixelsPerInch>")
                .Append("</o:OfficeDocumentSettings></xml></noscript><![endif]-->\r\n");

            // Refinements only. A client that drops this block still gets the full design at the
            // card's fixed width, which every mobile client then shrinks to fit.
            html.Append("<style>\r\n")
                .Append("@media only screen and (max-width:620px){\r\n")
                .Append("  .tmi-card{width:100% !important}\r\n")
                .Append("  .tmi-pad{padding-left:24px !important;padding-right:24px !important}\r\n")
                .Append("  .tmi-heading{font-size:24px !important;line-height:32px !important}\r\n")
                .Append("  .tmi-code{font-size:26px !important;letter-spacing:6px !important}\r\n")
                .Append("}\r\n")
                .Append("</style>\r\n")
                .Append("</head>\r\n");

            html.Append(CultureInfo.InvariantCulture, $"<body style=\"margin:0;padding:0;width:100%;background-color:{PageBackground};\">\r\n");

            AppendPreheader(html, content.Preheader);

            // The pane, then the card centred inside it.
            html.Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" dir=\"{dir}\" style=\"background-color:{PageBackground};border-collapse:collapse;\">\r\n")
                .Append("<tr><td align=\"center\" style=\"padding:24px 12px;\">\r\n")
                // The card carries its own edge, which is what separates it from the pane behind it
                // now that nothing else does.
                .Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"{CardWidth}\" class=\"tmi-card\" dir=\"{dir}\" style=\"width:{CardWidth}px;max-width:100%;background-color:{White};border:1px solid {CardBorder};border-collapse:collapse;\">\r\n");

            AppendBanner(html, productName, start);
            AppendBody(html, content, start, rtl);
            AppendFooter(html, content, start);

            html.Append("</table>\r\n")
                .Append("</td></tr>\r\n")
                .Append("</table>\r\n")
                .Append("</body>\r\n")
                .Append("</html>\r\n");

            return html.ToString();
        }

        /// <summary>
        ///     Writes the hidden line inboxes show beside the subject, followed by enough zero-width
        ///     space to stop the client filling the rest of the preview with the heading behind it.
        /// </summary>
        private static void AppendPreheader(StringBuilder html, string preheader)
        {
            html.Append("<div style=\"display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;\">")
                .Append(Encode(preheader))
                .Append(string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 30)))
                .Append("</div>\r\n");
        }

        /// <summary>Writes the ink banner and the brand mark inside it.</summary>
        private static void AppendBanner(StringBuilder html, string productName, string start)
        {
            // bgcolor as well as the inline style: Outlook honours the attribute more reliably than
            // the property on a table cell, and a banner that loses its ink turns white text white.
            html.Append(CultureInfo.InvariantCulture, $"<tr><td align=\"{start}\" bgcolor=\"{Ink}\" class=\"tmi-pad\" style=\"background-color:{Ink};padding:20px 32px;\">\r\n")
                .Append(CultureInfo.InvariantCulture, $"<img src=\"cid:{EmailWordmark.ContentId}\" width=\"{EmailWordmark.DisplayWidth}\" height=\"{EmailWordmark.DisplayHeight}\" alt=\"{Encode(productName)}\" ")
                // The alt text is styled too, so a client that strips the part still shows the brand
                // in white rather than in the client's default near-black on near-black ink.
                .Append(CultureInfo.InvariantCulture, $"style=\"display:block;border:0;outline:none;text-decoration:none;color:{White};font-family:{FontStack};font-size:20px;font-weight:700;\" />\r\n")
                .Append("</td></tr>\r\n");
        }

        /// <summary>Writes the white body: heading, prose, the action, and the small print.</summary>
        private static void AppendBody(StringBuilder html, EmailContent content, string start, bool rtl)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td align=\"{start}\" class=\"tmi-pad\" style=\"background-color:{White};padding:36px 32px 32px;\">\r\n");

            // Heading. An h1 rather than a styled paragraph: a heading is what it is, and mail is
            // read by screen readers too.
            html.Append(CultureInfo.InvariantCulture, $"<h1 class=\"tmi-heading\" style=\"margin:0 0 20px;font-family:{FontStack};font-size:28px;line-height:36px;font-weight:700;color:{Ink};mso-line-height-rule:exactly;\">")
                .Append(Encode(content.Heading))
                .Append("</h1>\r\n");

            foreach (string paragraph in content.Paragraphs)
            {
                AppendParagraph(html, paragraph, BodyText, 16, 26);
            }

            if (content.Code is not null)
            {
                AppendCodePanel(html, content.Code, content.CodeNote);
            }

            if (content.Action is not null)
            {
                AppendButton(html, content.Action, start);
            }

            // One rule separates the message from its small print, wherever that print comes from:
            // the address behind the button, the notes, or both. Emitting it per section would draw
            // two of them on any message that has both.
            if (content.Action is not null || content.Notes.Count > 0)
            {
                AppendDivider(html);
            }

            if (content.Action is not null)
            {
                AppendActionFallback(html, content.Action, rtl);
            }

            foreach (string note in content.Notes)
            {
                AppendParagraph(html, note, SecondaryText, 14, 22);
            }

            html.Append("</td></tr>\r\n");
        }

        /// <summary>Writes the panel holding a one-time code, and its caption.</summary>
        private static void AppendCodePanel(StringBuilder html, string code, string? codeNote)
        {
            html.Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"border-collapse:separate;margin:4px 0 20px;\">\r\n")
                .Append(CultureInfo.InvariantCulture, $"<tr><td align=\"center\" bgcolor=\"{CodeBackground}\" style=\"background-color:{CodeBackground};border:1px solid {CodeBorder};border-radius:10px;padding:22px 16px;\">\r\n")
                // dir on the element, not CSS: a code is digits, and digits keep their order under
                // a right-to-left paragraph only when the run is isolated explicitly.
                .Append(CultureInfo.InvariantCulture, $"<span class=\"tmi-code\" dir=\"ltr\" style=\"font-family:{MonoStack};font-size:32px;line-height:40px;font-weight:700;letter-spacing:10px;color:{Ink};mso-line-height-rule:exactly;\">")
                .Append(Encode(code))
                .Append("</span>\r\n")
                .Append("</td></tr>\r\n")
                .Append("</table>\r\n");

            if (codeNote is not null)
            {
                AppendParagraph(html, codeNote, SecondaryText, 14, 22);
            }
        }

        /// <summary>Writes the button.</summary>
        private static void AppendButton(StringBuilder html, EmailAction action, string start)
        {
            // The padding sits on the cell, not on the anchor: Outlook ignores padding on an inline
            // element, which would collapse the button to bare text on the client most likely to be
            // reading it. Trading the anchor's own hit area for one that renders everywhere.
            // The bottom margin matches what a paragraph leaves behind, so the rule under the button
            // sits at the same distance whether prose or a button precedes it.
            html.Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-collapse:separate;margin:8px 0 16px;\" align=\"{start}\">\r\n")
                .Append(CultureInfo.InvariantCulture, $"<tr><td align=\"center\" bgcolor=\"{Primary}\" style=\"background-color:{Primary};border-radius:6px;padding:14px 28px;\">\r\n")
                .Append(CultureInfo.InvariantCulture, $"<a href=\"{SafeUrl(action.Url)}\" style=\"font-family:{FontStack};font-size:16px;line-height:20px;font-weight:700;color:{White};text-decoration:none;display:inline-block;mso-line-height-rule:exactly;\">")
                .Append(Encode(action.Label))
                .Append("</a>\r\n")
                .Append("</td></tr>\r\n")
                .Append("</table>\r\n");
        }

        /// <summary>Writes the note introducing the button's address, and the address itself.</summary>
        private static void AppendActionFallback(StringBuilder html, EmailAction action, bool rtl)
        {
            // One sentence: how long the link lasts, then what to do when the button will not open
            // it. Two paragraphs would put a break between halves of the same thought.
            AppendParagraph(html, action.Validity + " " + action.FallbackNote, SecondaryText, 14, 22);

            // The address is Latin and must not be reordered by a right-to-left paragraph, so the
            // run is isolated and then aligned back to the reader's own starting edge. word-break
            // lets a long one wrap instead of stretching the card past its width.
            string alignment = rtl ? "right" : "left";
            html.Append(CultureInfo.InvariantCulture, $"<p dir=\"ltr\" style=\"margin:0 0 16px;font-family:{FontStack};font-size:14px;line-height:22px;text-align:{alignment};word-break:break-all;mso-line-height-rule:exactly;\">")
                .Append(CultureInfo.InvariantCulture, $"<a href=\"{SafeUrl(action.Url)}\" style=\"color:{Primary};text-decoration:underline;\">")
                .Append(Encode(action.Url))
                .Append("</a></p>\r\n");
        }

        /// <summary>Writes one paragraph of prose.</summary>
        private static void AppendParagraph(StringBuilder html, string text, string color, int size, int lineHeight)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 16px;font-family:{FontStack};font-size:{size}px;line-height:{lineHeight}px;color:{color};mso-line-height-rule:exactly;\">")
                .Append(Encode(text))
                .Append("</p>\r\n");
        }

        /// <summary>Writes the hairline between the message and its small print.</summary>
        private static void AppendDivider(StringBuilder html)
        {
            // A bordered cell rather than an <hr>, which clients style inconsistently and Outlook
            // draws with its own engraved look.
            html.Append(CultureInfo.InvariantCulture, $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"border-collapse:collapse;margin:12px 0 20px;\">\r\n")
                .Append(CultureInfo.InvariantCulture, $"<tr><td style=\"height:1px;line-height:1px;font-size:0;background-color:{SubtleBorder};\">&nbsp;</td></tr>\r\n")
                .Append("</table>\r\n");
        }

        /// <summary>Writes the grey footer.</summary>
        private static void AppendFooter(StringBuilder html, EmailContent content, string start)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td align=\"{start}\" bgcolor=\"{FooterBackground}\" class=\"tmi-pad\" style=\"background-color:{FooterBackground};padding:24px 32px;\">\r\n")
                .Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0;font-family:{FontStack};font-size:13px;line-height:20px;color:{SecondaryText};mso-line-height-rule:exactly;\">")
                .Append(Encode(content.FooterNote))
                .Append("</p>\r\n");

            if (content.FooterLinks.Count > 0)
            {
                html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:10px 0 0;font-family:{FontStack};font-size:13px;line-height:20px;color:{SecondaryText};mso-line-height-rule:exactly;\">");

                bool first = true;
                foreach (EmailLink link in content.FooterLinks)
                {
                    if (!first)
                    {
                        // A separator the reader sees as punctuation, spaced so it survives the
                        // collapsing that some clients do to runs of whitespace.
                        html.Append("&nbsp;&nbsp;&middot;&nbsp;&nbsp;");
                    }

                    html.Append(CultureInfo.InvariantCulture, $"<a href=\"{SafeUrl(link.Url)}\" style=\"color:{SecondaryText};text-decoration:underline;\">")
                        .Append(Encode(link.Label))
                        .Append("</a>");
                    first = false;
                }

                html.Append("</p>\r\n");
            }

            html.Append("</td></tr>\r\n");
        }

        /// <summary>
        ///     Encodes text for HTML. Everything interpolated goes through here: the templates hold
        ///     prose, never markup, so encoding the rendered string is always right and is what keeps
        ///     a display name or an address from closing an attribute.
        /// </summary>
        private static string Encode(string value)
        {
            return WebUtility.HtmlEncode(value);
        }

        /// <summary>Encodes a link target, refusing any scheme a mail client should not follow.</summary>
        /// <remarks>
        ///     Every address here is minted by the server itself, so a failure is a defect rather
        ///     than an attack — but a link in mail is the one place a wrong scheme travels furthest,
        ///     and the check costs nothing.
        /// </remarks>
        private static string SafeUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                ? Encode(url)
                : throw new ArgumentException(
                    $"An email link must be an absolute http or https address, but was '{url}'.", nameof(url));
        }
    }
}
