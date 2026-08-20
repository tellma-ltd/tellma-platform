// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Text.RegularExpressions;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The page a failure with nowhere else to go ends on, and the one thing on it an operator
    ///     can work with. A user reporting a problem can describe what they saw; only a reference
    ///     they can read off the screen turns that into a request in the logs.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ErrorPageTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task A_protocol_error_shows_its_code_and_a_reference()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iderrproto");
            using OidcFlowClient flow = new(factory);

            // An unknown client cannot be answered by redirecting to it — there is no registration
            // to take a callback from — so the error comes back through this page.
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri("/connect/authorize?client_id=not-registered", UriKind.Relative),
                TestContext.Current.CancellationToken);

            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
            Assert.Contains("invalid_client", html, StringComparison.Ordinal);
            AssertShowsATraceId(html);

            // A caller error, so the page must not tell the reader the fault was ours.
            Assert.DoesNotContain("The problem is on our side", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_request_that_matches_nothing_still_shows_a_reference()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iderr404");
            using OidcFlowClient flow = new(factory);

            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri("/Identity/Account/NoSuchPage", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            // Re-executed through the same page with no protocol details to show, which is the case
            // that used to leave a bare heading and nothing an operator could act on.
            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
            AssertShowsATraceId(html);
        }

        /// <summary>Asserts the page shows a reference an operator could actually look up.</summary>
        /// <param name="html">The rendered error page.</param>
        private static void AssertShowsATraceId(string html)
        {
            Assert.Contains("Reference", html, StringComparison.Ordinal);

            // The trace id in its own right — thirty-two hex characters — and not the activity's
            // full identifier, which wraps the same value in a version, a span and flags. Only the
            // bare form matches what the logs and a collector index the request under, so a
            // reference in any other shape is one the user can read out and nobody can find.
            // Anchored on the label, because a protocol error renders its own code in a <code>
            // element further up the page and an unanchored match would read that instead.
            Match reference = Regex.Match(html, "Reference: <code>([^<]+)</code>");
            Assert.True(reference.Success, "The page showed no reference at all.");
            Assert.Matches("^[0-9a-f]{32}$", reference.Groups[1].Value);
        }
    }
}
