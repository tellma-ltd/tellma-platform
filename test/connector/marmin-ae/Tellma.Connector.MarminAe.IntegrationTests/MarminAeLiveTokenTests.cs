// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>That the signature recipe is the one the vendor actually accepts.</summary>
    /// <remarks>
    ///     Everything else in this suite depends on it, and no offline vector can vouch for it: a
    ///     recipe can be internally consistent and still be the wrong one.
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class MarminAeLiveTokenTests
    {
        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Exchanges_the_signature_for_a_usable_token()
        {
            MarminAeLiveEnvironment.Report();

            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();

            // Reading a business profile is the cheapest call that proves the token works, and it
            // is the one the future adapter makes at startup anyway.
            MarminAeResponse<MarminAeBusinessProfile> profile = await session.Client.GetBusinessProfileAsync(
                MarminAeLiveEnvironment.ProfileId, TestContext.Current.CancellationToken);

            Assert.Equal(200, profile.StatusCode);
        }

        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Refuses_a_signature_computed_with_the_wrong_secret()
        {
            // Without this, an API that accepted anything would make every other pass here
            // meaningless.
            MarminAeClientOptions options = MarminAeLiveEnvironment.Options() with
            {
                ClientSecret = "sk_deliberately_wrong_secret",
            };

            using HttpClient httpClient = new();
            MarminAeTokenProvider provider = new(httpClient, options);

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => provider.GetTokenAsync(TestContext.Current.CancellationToken));

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"The vendor refused a wrong signature with {exception.StatusCode}: {exception.Detail?.Describe()}");
            Assert.True(
                exception.StatusCode is >= 400 and < 500,
                $"Expected the vendor to refuse a wrong signature, but it answered {exception.StatusCode}.");
        }
    }
}
