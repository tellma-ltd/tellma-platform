// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     Stamps a sandbox tenant's internal mail as test-originated. Such mail really is delivered
    ///     — it addresses staff of the sending system — so the marking is what keeps a recipient
    ///     from mistaking it for the real thing.
    /// </summary>
    /// <remarks>
    ///     The marker text is fixed English, not localized: it is an operational token, recognizable
    ///     and filterable across every locale, and the message contract carries no locale to localize
    ///     against. Every part of the marking is idempotent, so an outbox redispatch of an
    ///     already-marked message never stacks banners.
    /// </remarks>
    internal static class SandboxMarker
    {
        /// <summary>The subject prefix.</summary>
        internal const string SubjectPrefix = "[Sandbox] ";

        /// <summary>The sentence prepended to the text body and carried by the HTML banner.</summary>
        internal const string Sentence = "[Sandbox] This message was generated from a sandbox (test) environment.";

        /// <summary>The class name on the HTML banner, which also makes the marking idempotent.</summary>
        internal const string BannerClassName = "tellma-sandbox-banner";

        // Inline styling only, and no external resources: mail clients strip stylesheets and block
        // remote content, so anything else would render as an unstyled orphan line.
        private const string BannerHtml =
            "<div class=\"" + BannerClassName + "\" style=\"background-color:#fff3cd;border:1px solid #ffe69c;"
            + "color:#664d03;padding:12px;margin:0 0 16px;font-family:sans-serif;font-size:14px;\">"
            + Sentence
            + "</div>";

        // CRLF rather than Environment.NewLine: the marker must read identically on every host, and
        // CRLF is the line ending mail itself uses.
        private const string TextPrefix = Sentence + "\r\n\r\n";

        /// <summary>Returns the message marked as sandbox-originated.</summary>
        /// <param name="message">The message to mark.</param>
        /// <returns>A copy carrying the marking; marking an already-marked message changes nothing.</returns>
        internal static EmailMessage Mark(EmailMessage message)
        {
            return message with
            {
                Subject = MarkSubject(message.Subject),
                TextBody = MarkTextBody(message.TextBody),
                HtmlBody = message.HtmlBody is null ? null : MarkHtmlBody(message.HtmlBody),
            };
        }

        /// <summary>Prefixes the subject, unless it already carries the prefix.</summary>
        /// <param name="subject">The original subject.</param>
        /// <returns>The marked subject.</returns>
        internal static string MarkSubject(string subject)
        {
            return subject.StartsWith(SubjectPrefix, StringComparison.Ordinal)
                ? subject
                : SubjectPrefix + subject;
        }

        /// <summary>Prepends the marker sentence and a blank line to the text body.</summary>
        /// <param name="textBody">The original body.</param>
        /// <returns>The marked body.</returns>
        internal static string MarkTextBody(string textBody)
        {
            return textBody.StartsWith(Sentence, StringComparison.Ordinal)
                ? textBody
                : TextPrefix + textBody;
        }

        /// <summary>
        ///     Inserts the banner immediately after the first <c>&lt;body…&gt;</c> tag, or prepends it
        ///     when the fragment has no body element.
        /// </summary>
        /// <param name="htmlBody">The original HTML.</param>
        /// <returns>The marked HTML.</returns>
        internal static string MarkHtmlBody(string htmlBody)
        {
            if (htmlBody.Contains(BannerClassName, StringComparison.Ordinal))
            {
                return htmlBody;
            }

            int insertAt = FindBodyTagEnd(htmlBody);
            return insertAt < 0
                ? BannerHtml + htmlBody
                : string.Concat(htmlBody.AsSpan(0, insertAt + 1), BannerHtml, htmlBody.AsSpan(insertAt + 1));
        }

        /// <summary>
        ///     Finds the index of the '&gt;' closing the first <c>&lt;body&gt;</c> start tag, or -1.
        /// </summary>
        /// <param name="html">The HTML to scan.</param>
        /// <returns>The index of the closing angle bracket, or -1 when there is no body tag.</returns>
        /// <remarks>
        ///     A deliberately small scanner rather than a regex or an HTML parser. It rejects
        ///     look-alike element names (<c>&lt;bodyguard&gt;</c>), tracks attribute quoting so a
        ///     '&gt;' inside an attribute value does not end the tag early, and skips over comments
        ///     and script or style content, where the word <c>&lt;body&gt;</c> routinely appears
        ///     without being an element — a conditional comment for Outlook is the common case. A
        ///     banner inserted into one of those regions would render nowhere, and would then
        ///     suppress the real insertion, because the idempotency guard would find its own class
        ///     name in the dead markup.
        /// </remarks>
        internal static int FindBodyTagEnd(string html)
        {
            int searchFrom = 0;
            while (searchFrom < html.Length)
            {
                int relative = html.AsSpan(searchFrom).IndexOf('<');
                if (relative < 0)
                {
                    return -1;
                }

                int tagStart = searchFrom + relative;
                ReadOnlySpan<char> rest = html.AsSpan(tagStart);

                if (rest.StartsWith("<!--", StringComparison.Ordinal))
                {
                    int close = html.IndexOf("-->", tagStart + 4, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        // An unterminated comment swallows the remainder of the document, so there is
                        // no real body tag left to find.
                        return -1;
                    }

                    searchFrom = close + 3;
                    continue;
                }

                if (TrySkipRawTextElement(html, tagStart, "script", out int afterScript)
                    || TrySkipRawTextElement(html, tagStart, "style", out afterScript))
                {
                    searchFrom = afterScript;
                    continue;
                }

                if (rest.StartsWith("<body", StringComparison.OrdinalIgnoreCase))
                {
                    int afterName = tagStart + "<body".Length;

                    // "<bodyguard" is a different element; only a delimiter may follow the name.
                    if (afterName < html.Length
                        && (html[afterName] == '>' || html[afterName] == '/' || char.IsWhiteSpace(html[afterName])))
                    {
                        // FindTagEnd answers -1 for an unterminated start tag, which means the
                        // fragment is truncated and has no safe insertion point; the caller prepends.
                        return FindTagEnd(html, afterName);
                    }
                }

                searchFrom = tagStart + 1;
            }

            return -1;
        }

        /// <summary>
        ///     Recognizes a <c>script</c> or <c>style</c> start tag and reports where its content ends.
        /// </summary>
        /// <param name="html">The HTML being scanned.</param>
        /// <param name="tagStart">The index of the '&lt;' that may open the element.</param>
        /// <param name="name">The element name to match.</param>
        /// <param name="resumeAt">Where scanning should resume, when this returns true.</param>
        /// <returns>True when the element was recognized and skipped.</returns>
        private static bool TrySkipRawTextElement(string html, int tagStart, string name, out int resumeAt)
        {
            resumeAt = 0;
            ReadOnlySpan<char> rest = html.AsSpan(tagStart);
            if (!rest.StartsWith("<", StringComparison.Ordinal)
                || !rest[1..].StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int afterName = tagStart + 1 + name.Length;
            if (afterName >= html.Length
                || (html[afterName] != '>' && html[afterName] != '/' && !char.IsWhiteSpace(html[afterName])))
            {
                return false;
            }

            int tagEnd = FindTagEnd(html, afterName);
            if (tagEnd < 0)
            {
                // Truncated start tag: treat everything after it as raw text, which is what a mail
                // client would do too.
                resumeAt = html.Length;
                return true;
            }

            int close = html.IndexOf("</" + name, tagEnd + 1, StringComparison.OrdinalIgnoreCase);
            resumeAt = close < 0 ? html.Length : close + 2 + name.Length;
            return true;
        }

        private static int FindTagEnd(string html, int scanFrom)
        {
            char quote = '\0';
            for (int i = scanFrom; i < html.Length; i++)
            {
                char c = html[i];
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (c is '"' or '\'')
                {
                    quote = c;
                    continue;
                }

                if (c == '>')
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
