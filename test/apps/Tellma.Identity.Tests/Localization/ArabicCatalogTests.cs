// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Email;

namespace Tellma.Identity.Tests.Localization
{
    /// <summary>
    ///     Every Arabic string, rendered the way a user would receive it.
    ///     <para>
    ///         The formatter treats a malformed pattern as a content bug and returns it verbatim
    ///         rather than throwing — the right call on a sign-in page, but it means a typo inside
    ///         a gender select does not fail anywhere: it is delivered, as braces, to a mailbox or
    ///         onto the page. Nothing else in the suite would notice, because the strings that
    ///         carry a select are mostly not the ones a test reads back.
    ///     </para>
    /// </summary>
    public sealed partial class ArabicCatalogTests
    {
        /// <summary>Positional placeholders, which have to be supplied or the render fails.</summary>
        [GeneratedRegex(@"\{(\d+)\}")]
        private static partial Regex PositionalArgument { get; }

        /// <summary>The two Arabic catalogs, as theory rows of (resource name, pattern).</summary>
        public static TheoryData<string, string, string> ArabicStrings
        {
            get
            {
                TheoryData<string, string, string> rows = [];
                foreach ((string catalog, Type marker) in ((string, Type)[])
                    [("SharedResources", typeof(SharedResources)), ("EmailTemplates", typeof(EmailTemplates))])
                {
                    ResourceManager resources = new(marker.FullName!, marker.Assembly);
                    ResourceSet set = resources.GetResourceSet(
                        CultureInfo.GetCultureInfo("ar"), createIfNotExists: true, tryParents: false)
                        ?? throw new InvalidOperationException($"No Arabic resources for {catalog}.");

                    foreach (DictionaryEntry entry in set)
                    {
                        rows.Add(catalog, (string)entry.Key, (string)entry.Value!);
                    }
                }

                return rows;
            }
        }

        [Theory]
        [MemberData(nameof(ArabicStrings))]
        public void Every_arabic_string_renders_for_every_gender(string catalog, string name, string pattern)
        {
            foreach (string gender in (string[])["female", "male", "other"])
            {
                Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
                {
                    [IcuMessageFormatter.GenderArgument] = gender,
                };

                // Positional arguments are supplied as themselves, so a value that survives into
                // the output identifies which placeholder was dropped.
                foreach (Match match in PositionalArgument.Matches(pattern))
                {
                    arguments[match.Groups[1].Value] = "<" + match.Groups[1].Value + ">";
                }

                string rendered = IcuMessageFormatter.Format(pattern, arguments);

                Assert.False(
                    rendered.Contains('{', StringComparison.Ordinal),
                    $"{catalog}.{name} ({gender}) did not render: {rendered}");
            }
        }

        [Theory]
        [MemberData(nameof(ArabicStrings))]
        public void A_gendered_arabic_string_actually_differs_by_gender(string catalog, string name, string pattern)
        {
            if (!pattern.Contains(IcuMessageFormatter.GenderArgument, StringComparison.Ordinal))
            {
                return;
            }

            // A select whose branches read identically is a translation that was marked up and
            // then never varied — the shape a copy/paste leaves behind, and invisible in review
            // because the markup looks right.
            Assert.False(
                string.Equals(RenderAs(pattern, "female"), RenderAs(pattern, "other"), StringComparison.Ordinal),
                $"{catalog}.{name} selects on gender but reads the same either way.");
        }

        /// <summary>Renders a pattern for one gender, supplying every positional placeholder.</summary>
        private static string RenderAs(string pattern, string gender)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal)
            {
                [IcuMessageFormatter.GenderArgument] = gender,
            };

            foreach (Match match in PositionalArgument.Matches(pattern))
            {
                arguments[match.Groups[1].Value] = "<" + match.Groups[1].Value + ">";
            }

            return IcuMessageFormatter.Format(pattern, arguments);
        }
    }
}
