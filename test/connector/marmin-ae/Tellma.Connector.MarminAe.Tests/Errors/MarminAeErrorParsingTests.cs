// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Errors
{
    /// <summary>What can be read out of a refusal, and what survives when nothing can.</summary>
    /// <remarks>
    ///     The vendor publishes no schema for its error bodies and demonstrably uses more than one
    ///     shape — three, across three endpoints, in the recordings alone. So the property under
    ///     test is tolerance as much as fidelity: whatever arrives, the status is kept, whatever
    ///     detail is legible is read, the body is captured, and nothing throws.
    /// </remarks>
    public class MarminAeErrorParsingTests
    {
        public static TheoryData<string> EveryVector()
        {
            return
            [
                "Errors/400-field-validation",
                "Errors/400-described-list",
                "Errors/400-multi-valued-paths",
                "Errors/401-invalid-token",
                "Errors/404-unknown-document",
                "Errors/404-unknown-profile",
                "Errors/413-payload-too-large",
                "Errors/429-rate-limited",
                "Errors/500-internal",
                "Errors/problem-details",
                "Errors/502-gateway",
                "Errors/truncated",
                "Errors/empty",
            ];
        }

        [Fact]
        public async Task Reads_the_field_by_field_refusal_the_vendor_sends()
        {
            Vector vector = Vectors.Load("Errors/400-field-validation");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.BadRequest, vector.Content);

            Assert.Equal(400, exception.StatusCode);
            MarminAeErrorDetail detail = Assert.IsType<MarminAeErrorDetail>(exception.Detail);

            Assert.Equal(3, detail.Errors.Count);
            Assert.Contains(
                detail.Errors,
                error => error.Field == "profile_execution_id"
                    && error.Message == "Profile execution id must be 8 digits and only contain 0 or 1");
            Assert.Contains("document_currency_code:", detail.Describe(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Reads_a_refusal_that_carries_one_message_instead_of_a_field_list()
        {
            Vector vector = Vectors.Load("Errors/404-unknown-document");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.NotFound, vector.Content);

            Assert.Equal(
                "Sales invoice not found with id: 00000000-0000-0000-0000-000000000000",
                exception.Detail?.Message);
            Assert.Empty(exception.Detail!.Errors);
        }

        [Fact]
        public async Task Reads_a_refusal_that_puts_its_message_at_the_root()
        {
            // A third shape, on a third endpoint, from the same vendor on the same day.
            Vector vector = Vectors.Load("Errors/404-unknown-profile");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.NotFound, vector.Content);

            Assert.Equal("profile not found", exception.Detail?.Message);
        }

        [Fact]
        public async Task Reads_a_refusal_to_accept_the_bearer()
        {
            Vector vector = Vectors.Load("Errors/401-invalid-token");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.Unauthorized, vector.Content);

            Assert.Equal(401, exception.StatusCode);
            Assert.Equal("Invalid or expired token", exception.Detail?.Message);
        }

        [Fact]
        public async Task Reads_a_throttling_refusal()
        {
            Vector vector = Vectors.Load("Errors/429-rate-limited");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.TooManyRequests, vector.Content);

            Assert.True(exception.IsRateLimited);
            Assert.Equal("API rate limit exceeded", exception.Detail?.Message);
        }

        [Fact]
        public async Task Reads_a_refusal_that_names_a_path_per_message()
        {
            Vector vector = Vectors.Load("Errors/400-described-list");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.BadRequest, vector.Content);

            MarminAeErrorDetail detail = Assert.IsType<MarminAeErrorDetail>(exception.Detail);
            Assert.Equal(2, detail.Errors.Count);
            Assert.Contains(
                detail.Errors,
                error => error.Field == "document_lines[0].price.base_amount" && error.Code == "IBR-027");
            Assert.Contains(
                detail.Errors,
                error => error.Field == "accounting_customer_party.tin"
                    && error.Message == "TIN must be exactly 10 characters");
        }

        [Fact]
        public async Task Reads_a_path_that_was_given_several_messages()
        {
            Vector vector = Vectors.Load("Errors/400-multi-valued-paths");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.BadRequest, vector.Content);

            Assert.Equal(2, exception.Detail?.Errors.Count);
            Assert.All(
                exception.Detail!.Errors,
                error => Assert.Equal("attachments[0].fileType", error.Field));
        }

        [Fact]
        public async Task Reads_a_refusal_shaped_as_a_problem_document()
        {
            Vector vector = Vectors.Load("Errors/problem-details");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.BadRequest, vector.Content);

            Assert.Equal("The document could not be accepted", exception.Detail?.Message);
        }

        [Fact]
        public async Task Degrades_to_the_raw_body_when_the_refusal_is_not_json()
        {
            Vector vector = Vectors.Load("Errors/502-gateway");

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.BadGateway, vector.Content);

            Assert.Equal(502, exception.StatusCode);
            Assert.Empty(exception.Detail!.Errors);
            Assert.Null(exception.Detail.Message);
            Assert.Contains("502 Bad Gateway", exception.Detail.RawBody, StringComparison.Ordinal);
        }

        [Theory]
        [MemberData(nameof(EveryVector))]
        public async Task Keeps_the_status_and_the_body_whatever_the_refusal_looked_like(string logicalName)
        {
            Vector vector = Vectors.Load(logicalName);

            MarminAeRequestException exception = await RefuseAsync(HttpStatusCode.BadRequest, vector.Content);

            Assert.Equal(400, exception.StatusCode);
            Assert.NotNull(exception.Detail);
            // Kept verbatim, empty bodies included: what an operator needs to see is exactly what
            // arrived, and "the vendor said nothing" is itself information.
            Assert.Equal(vector.Content, exception.Detail.RawBody);
            Assert.False(exception.Detail.IsRawBodyTruncated, $"{vector.FileName} should not have been truncated.");
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("42")]
        [InlineData("\"a bare string\"")]
        [InlineData(/*lang=json,strict*/ "{\"errors\":null}")]
        [InlineData(/*lang=json,strict*/ "{\"errors\":42}")]
        [InlineData(/*lang=json,strict*/ "{\"errors\":[1,2,3]}")]
        [InlineData(/*lang=json,strict*/ "{\"errors\":{\"nested\":{\"deep\":\"value\"}}}")]
        [InlineData("   ")]
        public void Never_throws_over_a_body_it_cannot_make_sense_of(string body)
        {
            MarminAeErrorDetail detail = MarminAeErrorParser.Parse(body);

            Assert.NotNull(detail);
            Assert.Equal(body, detail.RawBody);
        }

        [Fact]
        public void Reads_a_bare_string_body_as_the_message()
        {
            Assert.Equal("something went wrong", MarminAeErrorParser.Parse("\"something went wrong\"").Message);
        }

        [Fact]
        public void Caps_the_body_it_keeps_and_says_that_it_did()
        {
            string enormous = "{\"note\":\"" + new string('x', MarminAeErrorParser.MaxRawBodyLength * 2) + "\"}";

            MarminAeErrorDetail detail = MarminAeErrorParser.Parse(enormous);

            Assert.True(detail.IsRawBodyTruncated);
            Assert.True(
                detail.RawBody!.Length < enormous.Length,
                "A body over the cap must be shortened, not kept whole.");
            Assert.EndsWith("[truncated]", detail.RawBody, StringComparison.Ordinal);
        }

        [Fact]
        public void Keeps_a_body_exactly_at_the_cap_whole()
        {
            string body = new('x', MarminAeErrorParser.MaxRawBodyLength);

            MarminAeErrorDetail detail = MarminAeErrorParser.Parse(body);

            Assert.False(detail.IsRawBodyTruncated);
            Assert.Equal(body, detail.RawBody);
        }

        [Theory]
        [InlineData(60)]
        [InlineData(200)]
        public void Reads_a_deeply_nested_refusal_without_running_out_of_stack(int depth)
        {
            // Sixty levels is inside what the reader will parse, so the parser really walks it; two
            // hundred is past its depth limit, so the malformed-body fallback catches it. Only the
            // first exercises the walk, which is why both are here.
            string body = string.Concat(Enumerable.Repeat("{\"errors\":", depth))
                + "\"deep\""
                + new string('}', depth);

            MarminAeErrorDetail detail = MarminAeErrorParser.Parse(body);

            Assert.NotNull(detail);
            Assert.Equal(body, detail.RawBody);
        }

        [Fact]
        public void Describes_a_refusal_that_carries_both_a_summary_and_the_fields_it_objected_to()
        {
            // Both shapes are attested separately, so a body carrying both is plausible — and the
            // summary is the half a person reads first.
            MarminAeErrorDetail detail = MarminAeErrorParser.Parse(
                /*lang=json,strict*/ """{"errors":{"message":"Validation failed","accounting_customer_party.tin":"must be 10 characters"}}""");

            string described = Assert.IsType<string>(detail.Describe());
            Assert.Contains("Validation failed", described, StringComparison.Ordinal);
            Assert.Contains("accounting_customer_party.tin: must be 10 characters", described, StringComparison.Ordinal);
        }

        [Fact]
        public void Describes_a_field_the_vendor_named_without_saying_what_was_wrong()
        {
            MarminAeErrorDetail detail = MarminAeErrorParser.Parse(
                /*lang=json,strict*/ """{"errors":[{"field":"document_lines"}]}""");

            Assert.Equal("document_lines", detail.Describe());
        }

        [Fact]
        public void Keeps_a_truncated_body_from_ending_in_half_a_character()
        {
            // Cutting at a fixed index can land between the halves of a surrogate pair, and a lone
            // surrogate is what a logger either mangles or refuses.
            string body = new string('a', MarminAeErrorParser.MaxRawBodyLength - 1)
                + "😀"
                + new string('b', 100);

            MarminAeErrorDetail detail = MarminAeErrorParser.Parse(body);

            Assert.True(detail.IsRawBodyTruncated);
            Assert.DoesNotContain(detail.RawBody!, char.IsSurrogate);
        }

        [Fact]
        public void Describes_a_refusal_with_neither_message_nor_fields_by_its_body()
        {
            MarminAeErrorDetail detail = MarminAeErrorParser.Parse("<html>gateway</html>");

            Assert.Equal("<html>gateway</html>", detail.Describe());
        }

        private static async Task<MarminAeRequestException> RefuseAsync(HttpStatusCode statusCode, string body)
        {
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(statusCode, body));

            if (statusCode == HttpStatusCode.Unauthorized)
            {
                // A refusal to authenticate buys one re-authentication, so the vendor has to say it
                // twice before the caller hears it.
                harness.Enqueue(MarminAeResponses.Json(statusCode, body));
            }

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice,
                    MarminAeOperations.DocumentId,
                    TestContext.Current.CancellationToken));

            // Named in the message so a failure says whether it was reading a capture or a guess.
            Assert.True(exception.Detail is not null, "The refusal carried no detail at all.");

            return exception;
        }
    }
}
