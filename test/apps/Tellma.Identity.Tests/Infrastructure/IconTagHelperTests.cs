// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text.Encodings.Web;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The icon element. Every glyph on every page comes through here, so the contract worth
    ///     pinning is the wrapper's geometry, how a caller's class survives, the difference
    ///     between a decorative glyph and one that carries meaning, and that a misspelled name
    ///     fails loudly rather than shipping a blank square.
    /// </summary>
    public sealed class IconTagHelperTests
    {
        [Fact]
        public void A_glyph_renders_the_canonical_wrapper()
        {
            string html = Render(new IconTagHelper { Name = "key-round" });

            Assert.Contains("viewBox=\"0 0 24 24\"", html, StringComparison.Ordinal);
            Assert.Contains("stroke=\"currentColor\"", html, StringComparison.Ordinal);
            Assert.Contains("stroke-width=\"1.75\"", html, StringComparison.Ordinal);
            Assert.Contains("<path d=", html, StringComparison.Ordinal);
        }

        [Fact]
        public void A_glyph_without_a_label_is_decorative()
        {
            string html = Render(new IconTagHelper { Name = "mail" });

            Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("role=\"img\"", html, StringComparison.Ordinal);
        }

        [Fact]
        public void A_labelled_glyph_carries_its_own_name()
        {
            string html = Render(new IconTagHelper { Name = "log-out", Label = "Sign out" });

            Assert.Contains("role=\"img\"", html, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Sign out\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("aria-hidden", html, StringComparison.Ordinal);
        }

        [Fact]
        public void A_class_written_at_the_call_site_survives()
        {
            string html = Render(new IconTagHelper { Name = "arrow-left" }, ("class", "tmi-icon-sm tmi-icon-flip"));

            Assert.Contains("class=\"tmi-icon tmi-icon-sm tmi-icon-flip\"", html, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unregistered_name_is_an_error_rather_than_an_empty_square()
        {
            Assert.Throws<InvalidOperationException>(() => Render(new IconTagHelper { Name = "no-such-glyph" }));
        }

        /// <summary>Runs the tag helper and returns the markup it produced.</summary>
        /// <param name="helper">The configured helper.</param>
        /// <param name="attributes">Attributes the call site wrote on the element.</param>
        /// <returns>The rendered element.</returns>
        private static string Render(IconTagHelper helper, params (string Name, string Value)[] attributes)
        {
            TagHelperAttributeList supplied = [.. attributes.Select(a => new TagHelperAttribute(a.Name, a.Value))];
            TagHelperContext context = new(supplied, new Dictionary<object, object>(), "test");
            TagHelperOutput output = new(
                "tmi-icon",
                supplied,
                (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

            helper.Process(context, output);

            using StringWriter writer = new();
            output.WriteTo(writer, HtmlEncoder.Default);
            return writer.ToString();
        }
    }
}
