// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Picks the one brand font face a request should start downloading immediately.
    ///     <para>
    ///         The vendored faces carry a <c>unicode-range</c>, so a browser fetches only the
    ///         scripts a page actually renders — but it cannot know which those are until the font
    ///         stylesheet has been fetched, parsed, and matched against laid-out text. That delay
    ///         is what makes the fallback face visible before the brand face swaps in. The server
    ///         has no such uncertainty: the response culture already says which script the page is
    ///         about to render, so exactly one face is preloaded and the rest stay on demand.
    ///     </para>
    /// </summary>
    public static class FontPreload
    {
        /// <summary>The Latin face, used for every culture without a script of its own.</summary>
        private const string LatinFace = "~/_content/Tellma.Identity/fonts/noto-sans-latin-wght-normal.woff2";

        /// <summary>
        ///     The face to preload per script, keyed by the ISO 15924 script a culture writes in.
        ///     A culture whose script is absent falls back to Latin rather than preloading nothing:
        ///     a wrong guess costs one unused request, while preloading nothing costs the swap on
        ///     every page.
        /// </summary>
        private static readonly Dictionary<string, string> FacesByScript = new(StringComparer.Ordinal)
        {
            ["Arab"] = "~/_content/Tellma.Identity/fonts/noto-sans-arabic-arabic-wght-normal.woff2",
        };

        /// <summary>Resolves the face to preload for a culture.</summary>
        /// <param name="culture">The culture the response is rendered in.</param>
        /// <returns>An application-relative path to the woff2 file.</returns>
        public static string ForCulture(CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);

            return FacesByScript.TryGetValue(ScriptOf(culture), out string? face) ? face : LatinFace;
        }

        /// <summary>
        ///     The script a culture is written in. .NET exposes no script property, so this reads
        ///     the explicit script subtag when the culture name carries one (<c>az-Latn-AZ</c>) and
        ///     otherwise maps the language, which is unambiguous for the scripts we ship.
        /// </summary>
        private static string ScriptOf(CultureInfo culture)
        {
            string[] parts = culture.Name.Split('-');
            return parts.Length > 1 && parts[1].Length == 4
                ? parts[1]
                : parts[0] switch
                {
                    "ar" => "Arab",
                    _ => "Latn",
                };
        }
    }
}
