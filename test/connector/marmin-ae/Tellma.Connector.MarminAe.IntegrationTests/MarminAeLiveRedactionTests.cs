// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>That nothing identifying the account survives into a published run.</summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately not gated on credentials and deliberately untraited, so it runs on every
    ///         pull request. The rest of this suite only runs at night, and a redaction that is only
    ///         exercised on the runs that publish real data is a redaction nobody is checking.
    ///     </para>
    ///     <para>
    ///         The bodies below are shaped like the vendor's but carry invented values throughout.
    ///     </para>
    /// </remarks>
    public class MarminAeLiveRedactionTests
    {
        [Fact]
        public void Takes_the_registrations_and_the_address_out_of_a_business_profile()
        {
            const string body = /*lang=json,strict*/ """
                {"id":"11111111-2222-4333-8444-555555555555","status":"COMPLETED","name":"Falcon Trading LLC","tin":"1234567890123","company_id":"CN-4471902","email":"finance@falcon-trading.example","telephone":"+971500000000","endpoint_id":"784197300000001","postal_address":{"id":"99999999-8888-4777-8666-555555555555","street_name":"Sheikh Zayed Road","city_name":"Dubai","country_code":"AE"}}
                """;

            string scrubbed = MarminAeLiveRedaction.Scrub(body, []);

            // Everything the vendor's records say about who this taxpayer is.
            Assert.DoesNotContain("Falcon Trading LLC", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("1234567890123", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("CN-4471902", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("finance@falcon-trading.example", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("+971500000000", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("784197300000001", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("Sheikh Zayed Road", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("Dubai", scrubbed, StringComparison.Ordinal);

            // And nothing that makes the capture worth keeping.
            Assert.Contains("\"status\":\"COMPLETED\"", scrubbed, StringComparison.Ordinal);
            Assert.Contains("\"country_code\":\"AE\"", scrubbed, StringComparison.Ordinal);
        }

        [Fact]
        public void Takes_the_bearer_out_of_a_token_response()
        {
            const string body = /*lang=json,strict*/ """
                {"expires_in":3600,"token":"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJvcmdfMSJ9.c2lnbmF0dXJl"}
                """;

            string scrubbed = MarminAeLiveRedaction.Scrub(body, []);

            Assert.DoesNotContain("eyJ", scrubbed, StringComparison.Ordinal);

            // The shape is the whole reason this body is captured at all.
            Assert.Contains("\"expires_in\":3600", scrubbed, StringComparison.Ordinal);
        }

        [Fact]
        public void Reaches_the_same_number_inside_a_rendered_document()
        {
            // The rendering repeats every party detail, escaped, in one string. Substituting values
            // rather than rewriting fields is what makes this one pass rather than two.
            const string body = /*lang=json,strict*/ """
                {"tin":"1234567890123","document_xml":"<Invoice><CompanyID>1234567890123</CompanyID></Invoice>"}
                """;

            string scrubbed = MarminAeLiveRedaction.Scrub(body, []);

            Assert.DoesNotContain("1234567890123", scrubbed, StringComparison.Ordinal);
        }

        [Fact]
        public void Keeps_the_document_identifier_and_drops_the_addresss()
        {
            const string body = /*lang=json,strict*/ """
                {"id":"aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee","accounting_supplier_party":{"postal_address":{"id":"ffffffff-0000-4111-8222-333333333333","street_name":"Marina Walk"}}}
                """;

            string scrubbed = MarminAeLiveRedaction.Scrub(body, []);

            // Without the document identifier the capture cannot be tied to anything, and it names
            // a document rather than a taxpayer. The address identifier names the taxpayer.
            Assert.Contains("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", scrubbed, StringComparison.Ordinal);
            Assert.DoesNotContain("ffffffff-0000-4111-8222-333333333333", scrubbed, StringComparison.Ordinal);
        }

        [Fact]
        public void Scrubs_the_credentials_from_a_body_that_is_not_json_at_all()
        {
            // A gateway error page has no fields to walk, and is exactly the body most likely to
            // echo the query string back.
            const string body =
                "<html><body>Bad gateway: /auth/token?client_id=org_9f3c11ab</body></html>";

            string scrubbed = MarminAeLiveRedaction.Scrub(body, ["org_9f3c11ab"]);

            Assert.DoesNotContain("org_9f3c11ab", scrubbed, StringComparison.Ordinal);
            Assert.Contains("Bad gateway", scrubbed, StringComparison.Ordinal);
        }

        [Fact]
        public void Leaves_a_body_with_nothing_to_hide_exactly_as_it_arrived()
        {
            // Byte for byte, spacing and ordering included: the capture is a parser input, and a
            // reformatted one no longer evidences what the vendor sent.
            const string body = /*lang=json,strict*/ """
                {  "total_pages" : 3,
                   "content" : [ ] }
                """;

            Assert.Equal(body, MarminAeLiveRedaction.Scrub(body, []));
        }

        [Fact]
        public void Leaves_a_value_too_short_to_replace_safely_alone()
        {
            // Substituting a three-character value would take every innocent occurrence of it with
            // it, and a value that short is a code rather than an identity.
            const string body = /*lang=json,strict*/ """
                {"name":"AED","document_currency_code":"AED"}
                """;

            Assert.Equal(body, MarminAeLiveRedaction.Scrub(body, []));
        }

        [Fact]
        public void Survives_a_body_that_stops_halfway()
        {
            // Deliberately not annotated as JSON: the point of the case is that it is not.
            const string body = """
                {"tin":"1234567890123","nam
                """;

            string scrubbed = MarminAeLiveRedaction.Scrub(body, ["org_9f3c11ab"]);

            // Nothing to walk, so the tin survives — but a truncated body must not throw, and the
            // credentials must be scrubbed from it regardless.
            Assert.Equal(body, scrubbed);
        }

        [Theory]
        [InlineData("document_xml", true)]
        [InlineData("documentXml", true)]
        [InlineData("documentXML", true)]
        [InlineData("TIN", true)]
        [InlineData("party_name", true)]
        [InlineData("document_number", false)]
        [InlineData("status", false)]
        [InlineData("id", false)]
        public void Knows_which_field_names_carry_an_identity(string propertyName, bool expected)
        {
            // The vendor spells the same field three ways across its payloads, so the check has to
            // be spelling-blind or a snapshot leaks what a document response does not.
            Assert.Equal(expected, MarminAeLiveRedaction.IsAccountIdentifier(propertyName));
        }
    }
}
