// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Net;
using System.Text.Encodings.Web;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Adds the request's <see cref="CarriedQuery" /> to every link and form in an identity
    ///     view that leads to another identity page.
    ///     <para>
    ///         One rule applied to the rendered markup, rather than a route value repeated on each
    ///         link: a page added later keeps the user's language and their way back without
    ///         having to remember to, and a link that forgot could only be found by using the
    ///         pages in a second language. It runs after the framework's own link and form
    ///         helpers, so it sees the final address whichever attributes produced it.
    ///     </para>
    /// </summary>
    /// <param name="links">Decides which addresses are identity pages, and adds the values.</param>
    [HtmlTargetElement("a")]
    [HtmlTargetElement("form")]
    [HtmlTargetElement("button", Attributes = "formaction")]
    [HtmlTargetElement("button", Attributes = "asp-page")]
    [HtmlTargetElement("button", Attributes = "asp-page-handler")]
    [HtmlTargetElement("input", Attributes = "formaction")]
    [HtmlTargetElement("input", Attributes = "asp-page")]
    [HtmlTargetElement("input", Attributes = "asp-page-handler")]
    public sealed class CarriedQueryTagHelper(IdentityPageLinks links) : TagHelper
    {
        /// <summary>The attributes that hold an address a browser navigates or submits to.</summary>
        private static readonly string[] AddressAttributes = ["href", "action", "formaction"];

        /// <summary>The view being rendered, for the request whose values are carried.</summary>
        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = null!;

        /// <inheritdoc />
        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            ArgumentNullException.ThrowIfNull(output);

            foreach (string name in AddressAttributes)
            {
                if (!output.Attributes.TryGetAttribute(name, out TagHelperAttribute? attribute)
                    || ReadAddress(attribute.Value) is not { } address)
                {
                    continue;
                }

                // Rewritten only when something was added, so every other attribute keeps the
                // exact value and encoding the view gave it.
                string carried = links.Carry(ViewContext.HttpContext, address);
                if (!string.Equals(carried, address, StringComparison.Ordinal))
                {
                    output.Attributes.SetAttribute(name, carried);
                }
            }
        }

        /// <summary>
        ///     Reads an attribute's value as the address a browser would see. A value the
        ///     framework's helpers generated is plain text; one written in the view arrives
        ///     already encoded for HTML, and is decoded so it can be judged and extended as a URL.
        /// </summary>
        /// <param name="value">The attribute value.</param>
        /// <returns>The address, or null when the attribute holds none.</returns>
        private static string? ReadAddress(object? value)
        {
            switch (value)
            {
                case string text:
                    return text;

                case HtmlString html:
                    return WebUtility.HtmlDecode(html.Value);

                case IHtmlContent content:
                    using (StringWriter writer = new())
                    {
                        content.WriteTo(writer, HtmlEncoder.Default);
                        return WebUtility.HtmlDecode(writer.ToString());
                    }

                default:
                    return null;
            }
        }
    }
}
