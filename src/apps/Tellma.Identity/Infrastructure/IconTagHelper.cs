// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Collections.Frozen;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Renders one of the UI's glyphs as inline SVG: <c>&lt;tmi-icon name="key-round" /&gt;</c>.
    ///     <para>
    ///         Inline markup rather than an image or an icon font, because the content-security
    ///         policy admits no third-party host and only inline SVG inherits <c>currentColor</c>,
    ///         so a glyph takes the color of the control it sits in. A tag helper rather than a
    ///         partial, because it resolves by assembly and so reads identically from a Razor Page
    ///         and an MVC view, and because a <c>class</c> written at the call site merges through
    ///         rather than being replaced.
    ///     </para>
    /// </summary>
    [HtmlTargetElement("tmi-icon", TagStructure = TagStructure.WithoutEndTag)]
    public sealed class IconTagHelper : TagHelper
    {
        /// <summary>
        ///     The glyph set, drawn from Lucide at a single 1.75 stroke weight in a 24 viewBox —
        ///     the same source and geometry the component library's own inline glyphs use.
        /// </summary>
        private static readonly FrozenDictionary<string, string> Glyphs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["globe"] = "<circle cx=\"12\" cy=\"12\" r=\"10\" /><path d=\"M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20\" /><path d=\"M2 12h20\" />",
            ["chevron-down"] = "<path d=\"m6 9 6 6 6-6\" />",
            ["mail"] = "<rect width=\"20\" height=\"16\" x=\"2\" y=\"4\" rx=\"2\" /><path d=\"m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7\" />",
            ["key-round"] = "<path d=\"M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z\" /><circle cx=\"16.5\" cy=\"7.5\" r=\".5\" fill=\"currentColor\" />",
            ["check"] = "<path d=\"M20 6 9 17l-5-5\" />",
            ["circle-check-big"] = "<path d=\"M21.801 10A10 10 0 1 1 17 3.335\" /><path d=\"m9 11 3 3L22 4\" />",
            ["circle-x"] = "<circle cx=\"12\" cy=\"12\" r=\"10\" /><path d=\"m15 9-6 6\" /><path d=\"m9 9 6 6\" />",
            ["circle-alert"] = "<circle cx=\"12\" cy=\"12\" r=\"10\" /><line x1=\"12\" x2=\"12\" y1=\"8\" y2=\"12\" /><line x1=\"12\" x2=\"12.01\" y1=\"16\" y2=\"16\" />",
            ["triangle-alert"] = "<path d=\"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3\" /><path d=\"M12 9v4\" /><path d=\"M12 17h.01\" />",
            ["info"] = "<circle cx=\"12\" cy=\"12\" r=\"10\" /><path d=\"M12 16v-4\" /><path d=\"M12 8h.01\" />",
            ["shield-check"] = "<path d=\"M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z\" /><path d=\"m9 12 2 2 4-4\" />",
            ["lock"] = "<rect width=\"18\" height=\"11\" x=\"3\" y=\"11\" rx=\"2\" ry=\"2\" /><path d=\"M7 11V7a5 5 0 0 1 10 0v4\" />",
            ["user-round"] = "<circle cx=\"12\" cy=\"8\" r=\"5\" /><path d=\"M20 21a8 8 0 0 0-16 0\" />",
            ["book-open"] = "<path d=\"M12 7v14\" /><path d=\"M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z\" />",
            ["pen-line"] = "<path d=\"M12 20h9\" /><path d=\"M16.376 3.622a1 1 0 0 1 3.002 3.002L7.368 18.635a2 2 0 0 1-.855.506l-2.872.838a.5.5 0 0 1-.62-.62l.838-2.872a2 2 0 0 1 .506-.854z\" />",
            ["tv"] = "<rect width=\"20\" height=\"15\" x=\"2\" y=\"7\" rx=\"2\" ry=\"2\" /><polyline points=\"17 2 12 7 7 2\" />",
            ["monitor"] = "<rect width=\"20\" height=\"14\" x=\"2\" y=\"3\" rx=\"2\" /><line x1=\"8\" x2=\"16\" y1=\"21\" y2=\"21\" /><line x1=\"12\" x2=\"12\" y1=\"17\" y2=\"21\" />",
            ["smartphone"] = "<rect width=\"14\" height=\"20\" x=\"5\" y=\"2\" rx=\"2\" ry=\"2\" /><path d=\"M12 18h.01\" />",
            ["log-out"] = "<path d=\"M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4\" /><polyline points=\"16 17 21 12 16 7\" /><line x1=\"21\" x2=\"9\" y1=\"12\" y2=\"12\" />",
            ["arrow-left"] = "<path d=\"m12 19-7-7 7-7\" /><path d=\"M19 12H5\" />",
            ["link-2"] = "<path d=\"M9 17H7A5 5 0 0 1 7 7h2\" /><path d=\"M15 7h2a5 5 0 1 1 0 10h-2\" /><line x1=\"8\" x2=\"16\" y1=\"12\" y2=\"12\" />",
            ["copy"] = "<rect width=\"14\" height=\"14\" x=\"8\" y=\"8\" rx=\"2\" ry=\"2\" /><path d=\"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2\" />",
            ["download"] = "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\" /><polyline points=\"7 10 12 15 17 10\" /><line x1=\"12\" x2=\"12\" y1=\"15\" y2=\"3\" />",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>The glyph name, kebab-cased after Lucide (for example <c>key-round</c>).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        ///     An accessible name. Omit it — the common case — and the glyph is marked decorative,
        ///     because an icon beside its own visible label only repeats that label.
        /// </summary>
        public string? Label { get; set; }

        /// <inheritdoc />
        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            ArgumentNullException.ThrowIfNull(output);

            output.TagName = "svg";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Attributes.SetAttribute("viewBox", "0 0 24 24");
            output.Attributes.SetAttribute("fill", "none");
            output.Attributes.SetAttribute("stroke", "currentColor");
            output.Attributes.SetAttribute("stroke-width", "1.75");
            output.Attributes.SetAttribute("stroke-linecap", "round");
            output.Attributes.SetAttribute("stroke-linejoin", "round");

            // focusable="false" keeps Internet Explorer's legacy SVG tab stop from reappearing in
            // any engine that still honors it; the aria treatment depends on whether the glyph
            // carries meaning of its own or merely decorates a label beside it.
            output.Attributes.SetAttribute("focusable", "false");
            if (Label is null)
            {
                output.Attributes.SetAttribute("aria-hidden", "true");
            }
            else
            {
                output.Attributes.SetAttribute("role", "img");
                output.Attributes.SetAttribute("aria-label", Label);
            }

            // A class written at the call site is already in Attributes; prepend the base class
            // rather than replacing it, so `class="tmi-icon-lg tmi-icon-flip"` sizes and mirrors
            // the glyph without losing what every glyph shares.
            string classes = "tmi-icon";
            if (output.Attributes.TryGetAttribute("class", out TagHelperAttribute? supplied)
                && supplied.Value?.ToString() is { Length: > 0 } value)
            {
                classes = classes + " " + value;
            }

            output.Attributes.SetAttribute("class", classes);

            // A name with no glyph is a typo, and one that would otherwise ship as a blank square.
            output.Content.SetHtmlContent(Glyphs.TryGetValue(Name, out string? body)
                ? body
                : throw new InvalidOperationException($"No icon is registered under the name '{Name}'."));
        }
    }
}
