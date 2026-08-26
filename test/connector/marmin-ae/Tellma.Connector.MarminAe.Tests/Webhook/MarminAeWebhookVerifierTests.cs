// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Tellma.Connector.MarminAe.Tests.Webhook
{
    /// <summary>What the verifier accepts, and everything it must not.</summary>
    public class MarminAeWebhookVerifierTests
    {
        private const string Secret = "whsec_test_0000000000000001";
        private const string OtherSecret = "whsec_test_0000000000000002";

        // printf '%s' '<body>' | openssl dgst -sha256 -hmac 'whsec_test_0000000000000001' -binary | openssl base64 -A
        private const string Body =
            /*lang=json,strict*/ """{"org_id":"80d3cd7d-25ff-4d49-a60c-5d30a0026692","event_type":"sale.invoice.update"}""";
        private const string BodySignature = "sx5EhZHJqFqsborzi/e0FBY9q4aprgJUThHX/uyXfXs=";
        private const string EmptyBodySignature = "gbIfKYRPwTDJU4Qz/gcanmW5vliuMlutRichTTy4c3I=";

        [Fact]
        public void Accepts_the_known_answer_signature_for_the_pinned_body()
        {
            Assert.True(MarminAeWebhookVerifier.Verify(Utf8(Body), BodySignature, [Secret]));
        }

        [Fact]
        public void Accepts_an_empty_body_signed_with_the_right_secret()
        {
            Assert.True(MarminAeWebhookVerifier.Verify([], EmptyBodySignature, [Secret]));
        }

        [Fact]
        public void Rejects_a_tampered_body()
        {
            string tampered = Body.Replace("update", "create", StringComparison.Ordinal);

            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(tampered), BodySignature, [Secret]));
        }

        [Fact]
        public void Rejects_a_body_that_differs_only_in_insignificant_whitespace()
        {
            // The one that matters most. A receiver that parses the payload and re-serializes it
            // before verifying rejects every genuine delivery, and the symptom looks exactly like a
            // misconfigured secret.
            string reformatted = Body.Replace(",", ", ", StringComparison.Ordinal);

            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(reformatted), BodySignature, [Secret]));
        }

        [Fact]
        public void Rejects_a_signature_made_with_a_different_secret()
        {
            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(Body), BodySignature, [OtherSecret]));
        }

        [Fact]
        public void Rejects_a_hex_encoded_signature()
        {
            string hex = Convert.ToHexString(Convert.FromBase64String(BodySignature));

            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(Body), hex, [Secret]));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not base64!")]
        [InlineData("c2hvcnQ=")]
        [InlineData("dG9vLWxvbmctdG8tYmUtYS1zaGEyNTYtbWFjLWJ5LWEtbG9uZy13YXktaW5kZWVk")]
        public void Rejects_a_malformed_signature_without_throwing(string? signature)
        {
            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(Body), signature, [Secret]));
        }

        [Fact]
        public void Accepts_a_signature_whose_padding_was_stripped_in_transit()
        {
            // Base64 without its trailing padding is still Base64, and the rejection would look
            // exactly like a wrong secret.
            string unpadded = BodySignature.TrimEnd('=');

            Assert.NotEqual(BodySignature, unpadded);
            Assert.True(MarminAeWebhookVerifier.Verify(Utf8(Body), unpadded, [Secret]));
        }

        [Fact]
        public void Rejects_a_mac_of_the_right_length_that_is_simply_wrong()
        {
            string wrong = Convert.ToBase64String(new byte[32]);

            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(Body), wrong, [Secret]));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Accepts_the_signing_secret_wherever_it_sits_in_the_rotation(int position)
        {
            // Rotation is the reason more than one secret is accepted at all, and a verifier that
            // only ever tries the first one drops every delivery until the deploy completes.
            List<string> secrets = [OtherSecret, "whsec_test_0000000000000003", "whsec_test_0000000000000004"];
            secrets.Insert(position, Secret);

            Assert.True(MarminAeWebhookVerifier.Verify(Utf8(Body), BodySignature, secrets));
        }

        [Fact]
        public void Rejects_when_no_secret_is_configured()
        {
            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(Body), BodySignature, []));
        }

        [Fact]
        public void Rejects_when_every_configured_secret_is_blank()
        {
            Assert.False(MarminAeWebhookVerifier.Verify(Utf8(Body), BodySignature, ["", "   "]));
        }

        [Fact]
        public void Refuses_a_missing_secret_list_outright()
        {
            Assert.Throws<ArgumentNullException>(
                () => MarminAeWebhookVerifier.Verify(Utf8(Body), BodySignature, null!));
        }

        [Fact]
        public void Verifies_a_body_that_is_not_valid_text_at_all()
        {
            // The signature covers bytes, and a receiver that decodes to a string first mangles
            // anything that is not well-formed text before it ever gets to the comparison.
            byte[] body = [0x7B, 0xFF, 0xFE, 0x7D];
            string signature = Convert.ToBase64String(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body));

            Assert.True(MarminAeWebhookVerifier.Verify(body, signature, [Secret]));
        }

        [Fact]
        public void Compares_the_macs_with_the_frameworks_fixed_time_comparison()
        {
            // The only executable form the constant-time promise has. Reading the IL is deliberate:
            // extracting the comparison into another type would break this, and that refactor is
            // exactly when the property gets quietly lost. A timing measurement would be the
            // alternative, and on a shared runner it is a coin flip that ends up skipped.
            MethodInfo verify = typeof(MarminAeWebhookVerifier).GetMethod(
                nameof(MarminAeWebhookVerifier.Verify), BindingFlags.Public | BindingFlags.Static)!;

            List<string> called = CalledMethods(verify);

            // Only the positive assertion. The scan is byte-naive — it cannot tell an opcode from
            // the low byte of a metadata token — so asserting the *absence* of a call would be a
            // build break waiting for an unrelated edit to trigger it.
            Assert.Contains(
                nameof(CryptographicOperations) + "." + nameof(CryptographicOperations.FixedTimeEquals),
                called);
        }

        private static List<string> CalledMethods(MethodInfo method)
        {
            byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
            Module module = method.Module;
            Type[] typeArguments = method.DeclaringType!.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : [];

            List<string> called = [];
            for (int index = 0; index < il.Length - 4; index++)
            {
                // 0x28 is call and 0x6F is callvirt; each is followed by a four-byte metadata token.
                if (il[index] is not (0x28 or 0x6F))
                {
                    continue;
                }

                int token = BitConverter.ToInt32(il, index + 1);
                try
                {
                    MethodBase? resolved = module.ResolveMethod(token, typeArguments, null);
                    if (resolved?.DeclaringType is Type declaring)
                    {
                        called.Add(declaring.Name + "." + resolved.Name);
                    }
                }
                catch (ArgumentException)
                {
                    // A byte that happened to look like an opcode; the scan is deliberately naive
                    // and only ever adds names, so a miss here cannot make the assertions pass.
                }
            }

            return called;
        }

        private static byte[] Utf8(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }
    }
}
