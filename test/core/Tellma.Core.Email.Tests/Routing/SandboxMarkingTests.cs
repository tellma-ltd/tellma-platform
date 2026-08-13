// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email.Tests.Routing
{
    /// <summary>
    ///     The marking is what keeps a sandbox tenant's real mail from being mistaken for the real
    ///     thing, and it must survive a redispatch without stacking.
    /// </summary>
    public class SandboxMarkingTests
    {
        [Fact]
        public void Prefixes_the_subject_once()
        {
            Assert.Equal("[Sandbox] Invoice 42", SandboxMarker.MarkSubject("Invoice 42"));
            Assert.Equal("[Sandbox] Invoice 42", SandboxMarker.MarkSubject("[Sandbox] Invoice 42"));
        }

        [Fact]
        public void Prepends_the_marker_sentence_to_the_text_body_once()
        {
            string marked = SandboxMarker.MarkTextBody("Hello.");

            Assert.StartsWith(SandboxMarker.Sentence, marked, StringComparison.Ordinal);
            Assert.EndsWith("Hello.", marked, StringComparison.Ordinal);

            // A platform-dependent line ending would make the marker differ between hosts.
            Assert.Contains("\r\n\r\n", marked, StringComparison.Ordinal);
            Assert.Equal(marked, SandboxMarker.MarkTextBody(marked));
        }

        [Fact]
        public void Prepends_the_banner_when_the_html_has_no_body_element()
        {
            string marked = SandboxMarker.MarkHtmlBody("<p>Hello</p>");

            Assert.StartsWith("<div class=\"" + SandboxMarker.BannerClassName, marked, StringComparison.Ordinal);
            Assert.EndsWith("<p>Hello</p>", marked, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("<html><body>Hello</body></html>")]
        [InlineData("<html><body class=\"x\">Hello</body></html>")]
        [InlineData("<html><body\n  id='m'>Hello</body></html>")]
        [InlineData("<html><BODY>Hello</BODY></html>")]
        public void Inserts_the_banner_immediately_after_the_body_start_tag(string html)
        {
            string marked = SandboxMarker.MarkHtmlBody(html);

            int bannerAt = marked.IndexOf(SandboxMarker.BannerClassName, StringComparison.Ordinal);
            int helloAt = marked.IndexOf("Hello", StringComparison.Ordinal);

            Assert.True(bannerAt > 0);
            Assert.True(bannerAt < helloAt, "The banner must come before the original body content.");
        }

        [Theory]
        // An Outlook conditional comment, which is boilerplate in most real email templates.
        [InlineData("<html><!--[if mso]><body>legacy<![endif]--><body>Hello</body></html>")]
        [InlineData("<html><!-- <body> is coming --><body>Hello</body></html>")]
        // A script that happens to build markup as a string.
        [InlineData("<html><script>var t = \"<body>\";</script><body>Hello</body></html>")]
        [InlineData("<html><style>/* <body> */</style><body>Hello</body></html>")]
        public void Is_not_fooled_by_a_body_tag_inside_a_comment_or_a_script(string html)
        {
            string marked = SandboxMarker.MarkHtmlBody(html);

            // The banner has to land in the real body. Inserted into a comment or a script it would
            // render nowhere — and would then suppress every later attempt, because the idempotency
            // guard would find its own class name in the dead markup.
            int bannerAt = marked.IndexOf(SandboxMarker.BannerClassName, StringComparison.Ordinal);
            int helloAt = marked.IndexOf("Hello", StringComparison.Ordinal);
            int realBodyAt = marked.LastIndexOf("<body>", StringComparison.OrdinalIgnoreCase);

            Assert.True(bannerAt > realBodyAt, "The banner must follow the real body start tag.");
            Assert.True(bannerAt < helloAt, "The banner must come before the original body content.");
        }

        [Fact]
        public void Prepends_the_banner_when_the_only_body_tag_is_inside_an_unterminated_comment()
        {
            // Nothing after an unterminated comment is markup, so there is no insertion point; the
            // banner still has to reach the recipient.
            string marked = SandboxMarker.MarkHtmlBody("<html><!-- <body>Hello");

            Assert.StartsWith("<div class=\"" + SandboxMarker.BannerClassName, marked, StringComparison.Ordinal);
        }

        [Fact]
        public void Is_not_fooled_by_a_closing_bracket_inside_an_attribute()
        {
            string marked = SandboxMarker.MarkHtmlBody("<html><body onload=\"if (a>b) {}\">Hello</body></html>");

            // Inserting at the first '>' would have split the attribute value.
            Assert.Contains("onload=\"if (a>b) {}\"><div", marked, StringComparison.Ordinal);
        }

        [Fact]
        public void Is_not_fooled_by_an_element_whose_name_merely_starts_with_body()
        {
            string marked = SandboxMarker.MarkHtmlBody("<bodyguard>Hello</bodyguard>");

            Assert.StartsWith("<div class=\"" + SandboxMarker.BannerClassName, marked, StringComparison.Ordinal);
        }

        [Fact]
        public void Prepends_when_the_body_start_tag_is_never_closed()
        {
            string marked = SandboxMarker.MarkHtmlBody("<html><body class=\"truncated");

            Assert.StartsWith("<div class=\"" + SandboxMarker.BannerClassName, marked, StringComparison.Ordinal);
        }

        [Fact]
        public void Does_not_stack_banners_on_a_redispatch()
        {
            string once = SandboxMarker.MarkHtmlBody("<html><body>Hello</body></html>");
            string twice = SandboxMarker.MarkHtmlBody(once);

            Assert.Equal(once, twice);
        }

        [Fact]
        public void Leaves_an_absent_html_body_absent()
        {
            EmailMessage marked = SandboxMarker.Mark(new EmailMessage
            {
                To = [new EmailAddress("a@example.com")],
                Subject = "S",
                TextBody = "B",
                Audience = EmailAudience.Internal,
            });

            Assert.Null(marked.HtmlBody);
            Assert.Equal("[Sandbox] S", marked.Subject);
        }
    }
}
