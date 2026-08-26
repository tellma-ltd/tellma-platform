// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Http.Headers;

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>What the vendor does with a bearer it never issued.</summary>
    /// <remarks>
    ///     <para>
    ///         The assumption the whole re-authentication design rests on: an unauthenticated
    ///         request is refused outright rather than partly performed, which is what makes
    ///         repeating it safe for a verb that would otherwise create a document. Nothing offline
    ///         can establish that.
    ///     </para>
    ///     <para>
    ///         Sent without the client, and therefore without obtaining a token first. Driving the
    ///         client here would spend a token request on the retry, and the token endpoint allows
    ///         only five a minute — the retry itself is pinned offline, where it costs nothing.
    ///     </para>
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class MarminAeLiveAuthenticationTests
    {
        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Refuses_a_bearer_it_never_issued()
        {
            MarminAeLiveEnvironment.Report();

            CancellationToken cancellation = TestContext.Current.CancellationToken;
            MarminAeClientOptions options = MarminAeLiveEnvironment.Options();

            using HttpClient httpClient = new();
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri(
                    options.NormalizedBaseAddress,
                    "api/business-profiles/" + Uri.EscapeDataString(MarminAeLiveEnvironment.ProfileId)));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", "eyJhbGciOiJIUzI1NiJ9.e30.not-a-signature-this-vendor-would-accept");
            request.Headers.TryAddWithoutValidation(MarminAeClient.VersionHeaderName, MarminAeClient.ApiVersion);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellation);

            // Scrubbed by hand, because this one call deliberately bypasses the shared transport
            // that would otherwise have done it: a refusal is the body most likely to quote back
            // what it refused.
            string body = MarminAeLiveRedaction.Scrub(
                await response.Content.ReadAsStringAsync(cancellation),
                MarminAeLiveEnvironment.AccountValues());

            TestContext.Current.AddAttachment("401-invalid-token.recorded.json", body);
            TestContext.Current.TestOutputHelper?.WriteLine($"{(int)response.StatusCode}: {body}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // The refusal has to say something, or the client has nothing to hand the caller and
            // nothing to write to a log. That the client can read what it says is asserted offline,
            // against the capture this test attaches.
            Assert.False(string.IsNullOrWhiteSpace(body));
        }
    }
}
