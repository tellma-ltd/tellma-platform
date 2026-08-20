// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     What one email says, in the pieces the HTML layout arranges: a label, a heading, prose,
    ///     at most one thing to act on, and the small print under it.
    /// </summary>
    /// <remarks>
    ///     The plain-text alternative stays one flowing string, because that is what plain text is.
    ///     HTML needs the seams: a heading has to be a heading and a link has to become a button, and
    ///     neither can be recovered from a paragraph that already has the link spliced into it.
    /// </remarks>
    internal sealed record EmailContent
    {
        /// <summary>
        ///     The line inboxes preview beside the subject. Hidden in the body itself — left unset,
        ///     clients fall back to scraping the first visible text, which would repeat the heading
        ///     the subject has already said.
        /// </summary>
        public required string Preheader { get; init; }

        /// <summary>The heading, which carries the message on its own when nothing else is read.</summary>
        public required string Heading { get; init; }

        /// <summary>The prose above the action, one entry per paragraph.</summary>
        public required IReadOnlyList<string> Paragraphs { get; init; }

        /// <summary>
        ///     The one-time code, shown in a panel of its own; null when the message carries none.
        ///     Rendered left-to-right whatever the reader's language, because it is a number.
        /// </summary>
        public string? Code { get; init; }

        /// <summary>
        ///     The caption under the code panel — how long the code lasts. Belongs with the panel
        ///     rather than in <see cref="Notes" />, because it qualifies the thing directly above it.
        /// </summary>
        public string? CodeNote { get; init; }

        /// <summary>The button, and the address behind it; null when there is nothing to click.</summary>
        public EmailAction? Action { get; init; }

        /// <summary>The small print under the divider, one entry per paragraph.</summary>
        public required IReadOnlyList<string> Notes { get; init; }

        /// <summary>The footer sentence saying why this message arrived.</summary>
        public required string FooterNote { get; init; }

        /// <summary>The links beside the footer note; empty when the deployment publishes none.</summary>
        public IReadOnlyList<EmailLink> FooterLinks { get; init; } = [];
    }

    /// <summary>
    ///     The single thing an email asks the reader to do, as a button plus the address written out
    ///     underneath it — a button alone strands a reader whose client refuses to follow it.
    /// </summary>
    /// <param name="Label">The button's text.</param>
    /// <param name="Url">The absolute address the button opens.</param>
    /// <param name="Validity">How long the address lasts and how often it may be used. Separate
    ///     from <paramref name="FallbackNote" /> because it is true of the message either way,
    ///     whereas the fallback only makes sense where a button was drawn.</param>
    /// <param name="FallbackNote">The sentence introducing the written-out address, for the
    ///     rendering that has a button to fall back from.</param>
    internal sealed record EmailAction(string Label, string Url, string Validity, string FallbackNote);

    /// <summary>A labelled link in the footer.</summary>
    /// <param name="Label">The link's text.</param>
    /// <param name="Url">The absolute address.</param>
    internal sealed record EmailLink(string Label, string Url);
}
