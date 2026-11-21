// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text.Encodings.Web;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The helper that keeps the carried query values on the links and forms a view renders.
    ///     It runs on every anchor and form of every identity view, so what matters is that it
    ///     extends the addresses that lead to an identity page — however the view produced them —
    ///     and leaves every other attribute exactly as it found it.
    /// </summary>
    public sealed class CarriedQueryTagHelperTests
    {
        /// <summary>A request that carries all three values.</summary>
        private const string CarryingQuery = "?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders&ui_locales=ar";

        /// <summary>What the three values add to a URL without a query, as HTML writes it.</summary>
        private const string CarriedSuffix = "client_id=acme-erp&amp;return_url=https%3A%2F%2Facme.test%2Forders&amp;ui_locales=ar";

        [Fact]
        public void A_generated_link_to_an_identity_page_is_extended()
        {
            // The framework's link helper leaves the address as plain text.
            string html = Render("a", CarryingQuery, ("class", "tmi-nav-item"), ("href", "/Identity/Manage/Passkeys"));

            Assert.Equal($"<a class=\"tmi-nav-item\" href=\"/Identity/Manage/Passkeys?{CarriedSuffix}\"></a>", html);
        }

        [Fact]
        public void A_link_written_in_the_view_is_extended()
        {
            // A value the view wrote arrives already encoded for HTML, ampersands included. It is
            // read as the URL it stands for and written back encoded once, not twice.
            string html = Render(
                "a", "?ui_locales=ar", ("href", new HtmlString("/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize&amp;methods=passkey")));

            Assert.Equal(
                "<a href=\"/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize&amp;methods=passkey&amp;ui_locales=ar\"></a>", html);
        }

        [Fact]
        public void A_forms_action_is_extended()
        {
            string html = Render("form", CarryingQuery, ("method", "post"), ("action", "/Identity/Manage/Passkeys?handler=Remove"));

            Assert.Equal($"<form method=\"post\" action=\"/Identity/Manage/Passkeys?handler=Remove&amp;{CarriedSuffix}\"></form>", html);
        }

        [Fact]
        public void A_buttons_own_action_is_extended()
        {
            string html = Render("button", "?ui_locales=ar", ("type", "submit"), ("formaction", "/Identity/Account/EmailCode?handler=Resend"));

            Assert.Equal("<button type=\"submit\" formaction=\"/Identity/Account/EmailCode?handler=Resend&amp;ui_locales=ar\"></button>", html);
        }

        [Fact]
        public void A_form_that_posts_to_the_page_it_is_on_is_left_alone()
        {
            // Such a form's action is the request's own address, which states the values already.
            string action = "/Identity/Manage" + CarryingQuery;

            string html = Render("form", CarryingQuery, ("method", "post"), ("action", action));

            Assert.Equal($"<form method=\"post\" action=\"/Identity/Manage?{CarriedSuffix}\"></form>", html);
        }

        [Theory]

        // The way back to the client, an in-page anchor, a protocol endpoint, the language
        // switch, and a link out to a policy page: none of them is an identity page.
        [InlineData("https://acme.test/orders")]
        [InlineData("#tmi-main")]
        [InlineData("/connect/authorize?client_id=acme-erp&request_uri=urn%3Aabc")]
        [InlineData("/identity/culture")]
        [InlineData("https://example.com/privacy")]
        public void A_link_that_leaves_the_identity_pages_is_left_alone(string href)
        {
            string html = Render("a", CarryingQuery, ("href", href));

            Assert.Equal($"<a href=\"{HtmlEncoder.Default.Encode(href)}\"></a>", html);
        }

        [Fact]
        public void A_value_that_was_not_changed_keeps_the_encoding_the_view_gave_it()
        {
            // An untouched attribute is not rewritten at all, so nothing about it is re-encoded.
            string html = Render("a", CarryingQuery, ("href", new HtmlString("https://acme.test/a?x=1&amp;y=2")));

            Assert.Equal("<a href=\"https://acme.test/a?x=1&amp;y=2\"></a>", html);
        }

        [Fact]
        public void A_request_that_carries_nothing_changes_nothing()
        {
            string html = Render("a", "?section=passkeys", ("href", "/Identity/Manage/Passkeys"));

            Assert.Equal("<a href=\"/Identity/Manage/Passkeys\"></a>", html);
        }

        [Fact]
        public void An_element_without_an_address_is_left_alone()
        {
            string html = Render("a", CarryingQuery, ("class", "tmi-link"), ("role", "button"));

            Assert.Equal("<a class=\"tmi-link\" role=\"button\"></a>", html);
        }

        [Fact]
        public void A_carried_value_cannot_break_out_of_the_attribute()
        {
            // The return address is whatever a caller wrote. It reaches the markup as one query
            // value, encoded for the URL and then for HTML, never as markup of its own.
            string html = Render(
                "a", "?return_url=%22%3E%3Cscript%3Ealert(1)%3C%2Fscript%3E&client_id=x%22%20onclick%3D%22y", ("href", "/Identity/Manage"));

            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick=\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, html.Count(character => character == '"'));
        }

        /// <summary>Runs the helper over an element and returns the markup it produced.</summary>
        /// <param name="tag">The element name.</param>
        /// <param name="query">The query string of the request the view is rendering for.</param>
        /// <param name="attributes">The element's attributes, as the helpers before this one left them.</param>
        /// <returns>The rendered element.</returns>
        private static string Render(string tag, string query, params (string Name, object Value)[] attributes)
        {
            DefaultHttpContext http = new();
            http.Request.QueryString = new QueryString(query);

            CarriedQueryTagHelper helper = new(new IdentityPageLinks(Microsoft.Extensions.Options.Options.Create(new TellmaIdentityOptions())))
            {
                ViewContext = new ViewContext { HttpContext = http },
            };

            TagHelperAttributeList supplied = [.. attributes.Select(a => new TagHelperAttribute(a.Name, a.Value))];
            TagHelperContext context = new(supplied, new Dictionary<object, object>(), "test");
            TagHelperOutput output = new(
                tag,
                [.. supplied],
                (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

            helper.Process(context, output);

            using StringWriter writer = new();
            output.WriteTo(writer, HtmlEncoder.Default);
            return writer.ToString();
        }
    }
}
