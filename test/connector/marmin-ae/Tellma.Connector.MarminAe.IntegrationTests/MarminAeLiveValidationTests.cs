// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>What the vendor's refusals actually look like.</summary>
    /// <remarks>
    ///     The vendor publishes no schema for its error bodies, so this is where the offline parser
    ///     suite gets its evidence: each case attaches the raw refusal, named for the vector it
    ///     becomes.
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public class MarminAeLiveValidationTests
    {
        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Reports_field_level_errors_for_a_deliberately_invalid_invoice()
        {
            MarminAeLiveEnvironment.Report();

            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();
            session.Transcript.NextAttachmentName = "400-field-validation.recorded.json";

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => session.Client.CreateSalesInvoiceAsync(
                    MarminAeLiveEnvironment.ProfileId,
                    MarminAeLiveDocuments.InvalidInvoice(),
                    TestContext.Current.CancellationToken));

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"status {exception.StatusCode}; detail: {exception.Detail?.Describe()}");

            Assert.Equal(400, exception.StatusCode);
            Assert.NotNull(exception.Detail);
            Assert.False(
                string.IsNullOrWhiteSpace(exception.Detail.RawBody),
                "A refusal must always keep its body, whatever could or could not be read out of it.");
            Assert.True(
                exception.Detail.Errors.Count > 0 || !string.IsNullOrWhiteSpace(exception.Detail.Message),
                $"Nothing could be read out of the refusal: {exception.Detail.RawBody}");
        }

        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Reports_a_typed_error_for_a_document_that_does_not_exist()
        {
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();
            session.Transcript.NextAttachmentName = "404-unknown-document.recorded.json";

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => session.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    "00000000-0000-0000-0000-000000000000",
                    TestContext.Current.CancellationToken));

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"status {exception.StatusCode}; detail: {exception.Detail?.Describe()}");

            Assert.True(
                exception.StatusCode is 400 or 403 or 404,
                $"Expected the vendor to refuse an unknown document, but it answered {exception.StatusCode}.");
        }

        [Fact(
            Skip = MarminAeLiveEnvironment.SkipReason,
            SkipUnless = nameof(MarminAeLiveEnvironment.HasCredentials),
            SkipType = typeof(MarminAeLiveEnvironment))]
        public async Task Reports_a_typed_error_for_a_business_profile_that_does_not_exist()
        {
            using MarminAeLiveSession session = MarminAeLiveEnvironment.CreateSession();
            session.Transcript.NextAttachmentName = "404-unknown-profile.recorded.json";

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => session.Client.GetBusinessProfileAsync(
                    "MBP-DOES-NOT-EXIST", TestContext.Current.CancellationToken));

            TestContext.Current.TestOutputHelper?.WriteLine(
                $"status {exception.StatusCode}; detail: {exception.Detail?.Describe()}");

            Assert.True(exception.StatusCode >= 400, "The vendor answered a nonexistent profile with a success.");
        }
    }
}
