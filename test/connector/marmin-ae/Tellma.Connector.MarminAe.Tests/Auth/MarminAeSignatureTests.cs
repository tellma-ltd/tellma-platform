// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Auth
{
    /// <summary>
    ///     Pins the one signature recipe this connector uses, against answers computed outside .NET.
    /// </summary>
    /// <remarks>
    ///     Every way of getting this wrong produces a signature that looks entirely plausible and
    ///     that the vendor rejects: hex instead of Base64, the key and the message the wrong way
    ///     round, a trailing newline from a shell, an ASCII round trip that mangles a non-ASCII
    ///     secret. The expected values below were produced by OpenSSL, not by the code under test,
    ///     because a vector computed the same way as the implementation proves only that the
    ///     implementation is self-consistent.
    /// </remarks>
    public class MarminAeSignatureTests
    {
        // printf '%s' 'org_test_0000000000000001' \
        //   | openssl dgst -sha256 -hmac 'sk_test_0000000000000001' -binary | openssl base64 -A
        private const string ClientId = "org_test_0000000000000001";
        private const string ClientSecret = "sk_test_0000000000000001";
        private const string ExpectedSignature = "Dno4lKzpexL4mDK5Sz7YCZOh8tt+yi7Y2IR9MKMwkK0=";
        private const string ExpectedHex = "0e7a3894ace97b12f89832b94b3ed80993a1f2db7eca2ed8d8847d30a33090ad";
        private const string SwappedKeyAndMessage = "6wl08ZpGyHC2Bw0zvxf0berjpmVrhL93E8hsKHRptFg=";
        private const string WithTrailingNewline = "0HPMU4wjSJg21G+Vaq4CQ3fN1U+jAZ8MMi9PqS01BbU=";

        [Fact]
        public void Produces_the_known_answer_for_the_pinned_credentials()
        {
            Assert.Equal(ExpectedSignature, MarminAeSignature.Compute(ClientSecret, ClientId));
        }

        [Fact]
        public void Keys_with_the_secret_and_messages_with_the_client_id_and_not_the_reverse()
        {
            // Both orderings are valid Base64 of the right length, so nothing about the value itself
            // would give the mistake away — only the vendor's refusal would.
            Assert.NotEqual(SwappedKeyAndMessage, MarminAeSignature.Compute(ClientSecret, ClientId));
            Assert.Equal(SwappedKeyAndMessage, MarminAeSignature.Compute(ClientId, ClientSecret));
        }

        [Fact]
        public void Encodes_the_mac_as_base64_rather_than_hex()
        {
            string signature = MarminAeSignature.Compute(ClientSecret, ClientId);

            Assert.NotEqual(ExpectedHex, signature, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(ExpectedHex, Convert.ToHexString(Convert.FromBase64String(signature)).ToLowerInvariant());
        }

        [Fact]
        public void Signs_the_client_id_exactly_with_nothing_appended()
        {
            Assert.NotEqual(WithTrailingNewline, MarminAeSignature.Compute(ClientSecret, ClientId));
            Assert.Equal(WithTrailingNewline, MarminAeSignature.Compute(ClientSecret, ClientId + "\n"));
        }

        [Fact]
        public void Keys_with_the_utf8_bytes_of_a_non_ascii_secret()
        {
            // Written with escapes rather than literal characters so the vector cannot drift with
            // the encoding this file happens to be saved in.
            const string unicodeSecret = "s\u00e9cr\u00e8t-\u00dcn\u00efcode";
            const string expected = "3mV7FvcNJHCqLo356ps09gcAabIYwaN3sacsfP6nMVM=";

            Assert.Equal(expected, MarminAeSignature.Compute(unicodeSecret, ClientId));
        }

        [Fact]
        public void Produces_a_different_signature_for_a_different_secret()
        {
            Assert.NotEqual(
                MarminAeSignature.Compute(ClientSecret, ClientId),
                MarminAeSignature.Compute(ClientSecret + "x", ClientId));
        }

        [Fact]
        public void Signs_a_secret_longer_than_the_stack_buffer_identically_to_a_short_one()
        {
            // The implementation keeps short credentials off the heap and rents a buffer for long
            // ones; the two paths must agree, and only a secret past the threshold exercises the
            // second.
            string longSecret = new('k', 4096);
            string expected = Convert.ToBase64String(
                System.Security.Cryptography.HMACSHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(longSecret),
                    System.Text.Encoding.UTF8.GetBytes(ClientId)));

            Assert.Equal(expected, MarminAeSignature.Compute(longSecret, ClientId));
        }

        [Fact]
        public async Task Sends_the_known_answer_signature_on_the_token_request()
        {
            using var harness = MarminAeHarness.Create(
                clientId: ClientId, clientSecret: ClientSecret);
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(
                ExpectedSignature,
                Assert.Single(harness.TokenRequests).Header(MarminAeTokenProvider.SignatureHeaderName));
        }
    }
}
