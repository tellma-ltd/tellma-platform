// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>That the configured business profile is real and ready to issue documents.</summary>
    /// <remarks>
    ///     The check the future adapter will make at startup, for the same reason: a profile that
    ///     does not exist, or has not finished onboarding, fails every submission afterwards with an
    ///     error that says nothing about the configuration being wrong.
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class MarminAeLiveBusinessProfileTests
    {
        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Retrieves_the_configured_profile()
        {
            MarminAeLiveEnvironment.Report();

            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();
            session.Transcript.NextAttachmentName = "business-profile.recorded.json";

            MarminAeResponse<MarminAeBusinessProfile> response = await session.Client.GetBusinessProfileAsync(
                MarminAeLiveEnvironment.ProfileId, TestContext.Current.CancellationToken);

            Assert.Equal(200, response.StatusCode);

            MarminAeBusinessProfile profile = response.Value;
            Assert.Equal(MarminAeLiveEnvironment.ProfileId, profile.ProfileId);
            Assert.False(string.IsNullOrWhiteSpace(profile.OrgId), "The profile named no organization.");
            Assert.False(string.IsNullOrWhiteSpace(profile.EndpointId), "The profile has no network address.");
            Assert.NotNull(profile.PostalAddress);

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"the profile is {profile.Status}, addressed under scheme {profile.EndpointSchemeId}");

            // A profile that has not finished onboarding cannot issue anything, which makes every
            // other live failure in this suite a red herring.
            Assert.Equal("COMPLETED", profile.Status);
        }

        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Reads_the_organization_identifier_as_a_uuid()
        {
            // The webhook payload types this as one, and a webhook is the one payload this suite
            // cannot provoke. Reading it here is the closest available evidence that it is.
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();

            MarminAeResponse<MarminAeBusinessProfile> response = await session.Client.GetBusinessProfileAsync(
                MarminAeLiveEnvironment.ProfileId, TestContext.Current.CancellationToken);

            Assert.True(
                Guid.TryParse(response.Value.OrgId, out _),
                $"The organization identifier '{response.Value.OrgId}' is not a UUID, which the webhook event type assumes it is.");
        }
    }
}
