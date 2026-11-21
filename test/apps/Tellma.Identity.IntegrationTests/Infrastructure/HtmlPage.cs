// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using AngleSharp;
using AngleSharp.Dom;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Reads a rendered identity page the way a browser would see it, for tests that assert
    ///     on what a page links to rather than on the text of its markup.
    /// </summary>
    public static class HtmlPage
    {
        /// <summary>The origin the in-memory host answers at, for resolving relative addresses.</summary>
        private static readonly Uri Host = new("http://localhost");

        /// <summary>Parses a page response into a document whose links resolve against its address.</summary>
        /// <param name="response">The page response.</param>
        /// <returns>The parsed document.</returns>
        public static async Task<IDocument> ReadAsync(HttpResponseMessage response)
        {
            ArgumentNullException.ThrowIfNull(response);

            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            IBrowsingContext context = BrowsingContext.New(Configuration.Default);
            return await context.OpenAsync(
                request => request.Content(html).Address(response.RequestMessage!.RequestUri),
                TestContext.Current.CancellationToken);
        }

        /// <summary>Splits an address on this host into its path and its query values.</summary>
        /// <param name="address">A relative address, or an absolute one on the test host.</param>
        /// <returns>The path, and the query values by name.</returns>
        public static (string Path, Dictionary<string, StringValues> Query) Split(string address)
        {
            Uri absolute = new(Host, address);
            Assert.Equal(Host.GetLeftPart(UriPartial.Authority), absolute.GetLeftPart(UriPartial.Authority));
            return (absolute.AbsolutePath, QueryHelpers.ParseQuery(absolute.Query));
        }

        /// <summary>
        ///     Every address on a page that leads to one of the engine's sign-in or account pages:
        ///     link targets, form actions, and the actions of buttons that override their form's.
        /// </summary>
        /// <param name="document">The page.</param>
        /// <param name="pathBase">The path base the engine is mounted at; empty at the root.</param>
        /// <returns>The addresses, each as the page wrote it.</returns>
        public static IReadOnlyList<string> IdentityPageAddresses(IDocument document, string pathBase = "")
        {
            ArgumentNullException.ThrowIfNull(document);

            List<string> addresses = [];
            foreach (IElement element in document.QuerySelectorAll("a[href], form[action], [formaction]"))
            {
                string? address = element.GetAttribute("href") ?? element.GetAttribute("action") ?? element.GetAttribute("formaction");
                if (address is not null
                    && (address.StartsWith(pathBase + "/Identity/Account", StringComparison.OrdinalIgnoreCase)
                        || address.StartsWith(pathBase + "/Identity/Manage", StringComparison.OrdinalIgnoreCase)))
                {
                    addresses.Add(address);
                }
            }

            return addresses;
        }
    }
}
