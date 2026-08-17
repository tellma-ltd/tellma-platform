// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Email;

namespace Tellma.Identity.Tests.Services.Email
{
    /// <summary>
    ///     The messages the identity server composes, in both of the forms it sends them.
    ///     <para>
    ///         The plain-text body is what the browser suites read a code or a link back out of, so
    ///         it is covered there by every flow that signs in. What no other suite can see is the
    ///         HTML alternative: nothing renders it, so a body that lost its link, leaked an
    ///         unescaped display name, or came out left-to-right in Arabic would reach a mailbox
    ///         before anything failed.
    ///     </para>
    /// </summary>
    public sealed class EmailTemplateServiceTests
    {
        private const string Link = "https://id.example.com/Identity/Account/Invitation?code=abc123";

        [Fact]
        public void Every_message_carries_both_a_text_and_an_html_body()
        {
            EmailTemplateService templates = CreateService();
            TellmaIdentityUser user = CreateUser();

            foreach (EmailMessage message in AllMessages(templates, user))
            {
                Assert.False(string.IsNullOrWhiteSpace(message.TextBody));
                Assert.NotNull(message.HtmlBody);

                // A complete document, not a fragment: the sandbox marker inserts its banner after
                // the body tag, and mail clients treat a bare fragment inconsistently.
                Assert.StartsWith("<!DOCTYPE html>", message.HtmlBody, StringComparison.Ordinal);
                Assert.Contains("<body", message.HtmlBody, StringComparison.Ordinal);
                Assert.EndsWith("</html>\r\n", message.HtmlBody, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_text_body_carries_every_part_the_message_has()
        {
            EmailContent content = new()
            {
                Preheader = "the-preheader",
                Heading = "the-heading",
                Paragraphs = ["first-paragraph", "second-paragraph"],
                Code = "91095144",
                CodeNote = "the-code-note",
                Notes = ["first-note", "second-note"],
                FooterNote = "the-footer-note",
                FooterLinks = [new EmailLink("Privacy", "https://example.com/privacy")],
            };

            string text = EmailTextLayout.Render(content);

            // Everything the HTML shows a reader is here too, which is the whole point of deriving
            // one from the other rather than writing the two separately.
            foreach (string part in (string[])
                ["the-heading", "first-paragraph", "second-paragraph", "91095144", "the-code-note",
                 "first-note", "second-note", "the-footer-note", "https://example.com/privacy"])
            {
                Assert.Contains(part, text, StringComparison.Ordinal);
            }

            // Except the preheader, which is the inbox's preview line. It exists to be read beside
            // the subject, and repeating it in the body would say the same thing twice.
            Assert.DoesNotContain("the-preheader", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_text_body_offers_the_action_as_an_address_rather_than_a_button()
        {
            EmailContent content = new()
            {
                Preheader = "p",
                Heading = "h",
                Paragraphs = [],
                Action = new EmailAction("Accept invitation", Link, "the-validity", "the-button-fallback"),
                Notes = [],
                FooterNote = "f",
            };

            string text = EmailTextLayout.Render(content);

            Assert.Contains("Accept invitation:\r\n" + Link, text, StringComparison.Ordinal);
            Assert.Contains("the-validity", text, StringComparison.Ordinal);

            // The one sentence deliberately left out: there is no button here to have failed, so
            // telling the reader what to do when it does not work would be nonsense.
            Assert.DoesNotContain("the-button-fallback", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_text_body_carries_the_link_and_the_code_it_is_scraped_for()
        {
            EmailTemplateService templates = CreateService();
            TellmaIdentityUser user = CreateUser();

            // The browser suites and the local inspection script both read these back out of the
            // text body, so a rendering that dropped either would break them and nothing else.
            Assert.Contains("91095144", templates.SignInCode(user, "91095144").TextBody, StringComparison.Ordinal);
            Assert.Contains(Link, templates.Invitation(user, Link, expiryDays: 7).TextBody, StringComparison.Ordinal);
            Assert.Contains(Link, templates.PasswordReset(user, Link).TextBody, StringComparison.Ordinal);
        }

        [Fact]
        public void The_text_body_separates_its_paragraphs_the_way_mail_does()
        {
            EmailTemplateService templates = CreateService();

            string text = templates.Invitation(CreateUser(), Link, expiryDays: 7).TextBody;

            // CRLF, not the host's line ending: the same message has to read identically whether it
            // was composed on Windows or Linux.
            Assert.Contains("\r\n\r\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\n\n\n", text.Replace("\r", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        [Fact]
        public void Every_message_carries_the_wordmark_the_html_references()
        {
            EmailTemplateService templates = CreateService();
            TellmaIdentityUser user = CreateUser();

            foreach (EmailMessage message in AllMessages(templates, user))
            {
                EmailAttachment mark = Assert.Single(message.Attachments);

                // An inline part rather than an ordinary attachment, and referenced by exactly the
                // content id it was given — the pair is the whole mechanism, and half of it is
                // silently a broken image.
                Assert.NotNull(mark.ContentId);
                Assert.Equal("image/png", mark.ContentType);
                Assert.NotEmpty(mark.Content.ToArray());
                Assert.Contains($"src=\"cid:{mark.ContentId}\"", message.HtmlBody, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_wordmark_is_a_png_and_is_read_only_once()
        {
            EmailAttachment first = EmailWordmark.Attachment();
            EmailAttachment second = EmailWordmark.Attachment();

            // The PNG signature, so a resource that was embedded but mangled fails here rather
            // than as an image that will not decode in a mailbox.
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], first.Content[..4].ToArray());

            // Same buffer both times: the bytes are immutable and every message asks for them.
            Assert.True(first.Content.Span == second.Content.Span);
        }

        [Fact]
        public void A_link_reaches_the_html_as_both_a_button_and_a_written_out_address()
        {
            EmailTemplateService templates = CreateService();

            string html = templates.Invitation(CreateUser(), Link, expiryDays: 7).HtmlBody!;

            // Twice: once behind the button, once spelled out for a client that will not follow it.
            Assert.Equal(2, CountOccurrences(html, $"href=\"{Link}\""));
            Assert.Contains(">Accept invitation</a>", html, StringComparison.Ordinal);

            // And the address is readable, not only clickable.
            Assert.Contains($">{Link}</a>", html, StringComparison.Ordinal);
        }

        [Fact]
        public void A_code_reaches_the_html_isolated_left_to_right()
        {
            EmailTemplateService templates = CreateService();

            string html = templates.SignInCode(CreateUser(locale: "ar"), "91095144").HtmlBody!;

            // Digits inside a right-to-left paragraph keep their order only when the run says so.
            Assert.Contains("dir=\"ltr\"", html, StringComparison.Ordinal);
            Assert.Contains(">91095144</span>", html, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("en", "ltr")]
        [InlineData("ar", "rtl")]
        public void The_document_takes_the_recipients_own_direction(string locale, string direction)
        {
            EmailTemplateService templates = CreateService();

            string html = templates.SignInCode(CreateUser(locale: locale), "91095144").HtmlBody!;

            Assert.Contains($"<html lang=\"{locale}\" dir=\"{direction}\"", html, StringComparison.Ordinal);
        }

        [Fact]
        public void An_arabic_recipient_gets_the_arabic_copy_in_the_html()
        {
            EmailTemplateService templates = CreateService();

            EmailMessage message = templates.Invitation(CreateUser(locale: "ar"), Link, expiryDays: 7);

            // The button label, so this pins the HTML-only strings rather than only the subject
            // the plain-text body already shares.
            Assert.Contains("قبول الدعوة", message.HtmlBody!, StringComparison.Ordinal);
            Assert.DoesNotContain("Accept invitation", message.HtmlBody, StringComparison.Ordinal);
        }

        [Fact]
        public void A_recipients_own_text_cannot_break_out_of_the_markup()
        {
            EmailTemplateService templates = CreateService();
            TellmaIdentityUser user = CreateUser();
            user.Email = "chars&\"<script>@example.com";

            // The address is interpolated into the sign-in copy and the footer, so it is the one
            // piece of recipient-controlled text that reaches the HTML body.
            string html = templates.SignInCode(user, "91095144").HtmlBody!;

            Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
            Assert.Contains("&amp;", html, StringComparison.Ordinal);
        }

        [Fact]
        public void The_footer_links_only_to_documents_the_deployment_publishes()
        {
            TellmaIdentityUser user = CreateUser();

            string without = CreateService().SignInCode(user, "91095144").HtmlBody!;
            Assert.DoesNotContain(">Privacy</a>", without, StringComparison.Ordinal);
            Assert.DoesNotContain(">Terms</a>", without, StringComparison.Ordinal);

            string with = CreateService(
                privacyUrl: "https://example.com/privacy", termsUrl: "https://example.com/terms")
                .SignInCode(user, "91095144").HtmlBody!;
            Assert.Contains("href=\"https://example.com/privacy\"", with, StringComparison.Ordinal);
            Assert.Contains("href=\"https://example.com/terms\"", with, StringComparison.Ordinal);
        }

        [Fact]
        public void A_link_that_is_not_a_web_address_is_refused_rather_than_sent()
        {
            EmailTemplateService templates = CreateService();

            // Nothing generates such a link today. The check is here because a link in mail is
            // followed later, elsewhere, by someone who cannot see where it came from.
            Assert.Throws<ArgumentException>(
                () => templates.PasswordReset(CreateUser(), "javascript:alert(1)"));
        }

        /// <summary>The three messages, so a rule that must hold for all of them is stated once.</summary>
        private static IEnumerable<EmailMessage> AllMessages(
            EmailTemplateService templates, TellmaIdentityUser user)
        {
            yield return templates.SignInCode(user, "91095144");
            yield return templates.Invitation(user, Link, expiryDays: 7);
            yield return templates.PasswordReset(user, Link);
        }

        /// <summary>Builds the service the way the host composes it.</summary>
        private static EmailTemplateService CreateService(string? privacyUrl = null, string? termsUrl = null)
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddLocalization();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            ServiceProvider provider = services.BuildServiceProvider();

            // The same decorator the host registers, so the ICU rendering under test is the real
            // one rather than a plain resx lookup that would pass a malformed select through.
            IStringLocalizer<EmailTemplates> localizer = new IcuStringLocalizer<EmailTemplates>(
                new StringLocalizer<EmailTemplates>(provider.GetRequiredService<IStringLocalizerFactory>()),
                provider.GetRequiredService<IHttpContextAccessor>());

            TellmaIdentityOptions options = new();
            options.Ui.PrivacyPolicyUrl = privacyUrl;
            options.Ui.TermsOfServiceUrl = termsUrl;

            return new EmailTemplateService(
                localizer, new DefaultBrandingResolver(), Microsoft.Extensions.Options.Options.Create(options));
        }

        /// <summary>A recipient with everything the templates interpolate.</summary>
        private static TellmaIdentityUser CreateUser(string locale = "en")
        {
            return new TellmaIdentityUser
            {
                Email = "recipient@example.com",
                DisplayName = "Recipient",
                Locale = locale,
            };
        }

        /// <summary>Counts non-overlapping occurrences of a needle.</summary>
        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            for (int at = haystack.IndexOf(needle, StringComparison.Ordinal);
                at >= 0;
                at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
