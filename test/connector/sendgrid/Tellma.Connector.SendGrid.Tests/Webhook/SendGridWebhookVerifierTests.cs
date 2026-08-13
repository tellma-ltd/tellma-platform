// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography;
using System.Text;

namespace Tellma.Connector.SendGrid.Tests.Webhook
{
    /// <summary>
    ///     Signature verification is the only authentication the event endpoint has, so it is tested
    ///     against signatures produced exactly the way SendGrid produces them: ECDSA over
    ///     SHA-256 of the timestamp followed by the raw body, DER-encoded.
    /// </summary>
    public class SendGridWebhookVerifierTests
    {
        private const string Timestamp = "1767225600";
        private static readonly byte[] Body = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """[{"event":"delivered"}]""");

        [Fact]
        public void Accepts_a_signature_made_with_the_configured_key()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            SendGridWebhookVerifier verifier = new([PublicKey(key)]);

            Assert.True(verifier.Verify(Body, Timestamp, Sign(key, Timestamp, Body)));
        }

        [Fact]
        public void Rejects_an_ieee_p1363_signature()
        {
            // The regression this whole test class exists for: SendGrid sends an ASN.1 DER
            // SEQUENCE{r,s}, and verifying with the default format would silently reject every
            // genuine delivery. If this ever passes, the verifier stopped naming the format.
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            SendGridWebhookVerifier verifier = new([PublicKey(key)]);

            byte[] payload = [.. Encoding.UTF8.GetBytes(Timestamp), .. Body];
            string p1363 = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256));

            Assert.False(verifier.Verify(Body, Timestamp, p1363));
        }

        [Fact]
        public void Rejects_a_tampered_body()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            SendGridWebhookVerifier verifier = new([PublicKey(key)]);
            string signature = Sign(key, Timestamp, Body);

            Assert.False(verifier.Verify(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """[{"event":"bounce"}]"""), Timestamp, signature));
        }

        [Fact]
        public void Rejects_a_tampered_timestamp()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            SendGridWebhookVerifier verifier = new([PublicKey(key)]);

            Assert.False(verifier.Verify(Body, "1767225601", Sign(key, Timestamp, Body)));
        }

        [Fact]
        public void Rejects_a_signature_from_a_different_key()
        {
            using var configured = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            SendGridWebhookVerifier verifier = new([PublicKey(configured)]);

            Assert.False(verifier.Verify(Body, Timestamp, Sign(other, Timestamp, Body)));
        }

        [Theory]
        [InlineData("not base64!")]
        [InlineData("")]
        public void Rejects_a_malformed_signature_without_throwing(string signature)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            SendGridWebhookVerifier verifier = new([PublicKey(key)]);

            Assert.False(verifier.Verify(Body, Timestamp, signature));
        }

        [Fact]
        public void Accepts_either_key_during_a_rotation_regardless_of_order()
        {
            // The rotation affordance: the old and new keys are both configured while events signed
            // with either are still in flight.
            using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            SendGridWebhookVerifier oldFirst = new([PublicKey(oldKey), PublicKey(newKey)]);
            SendGridWebhookVerifier newFirst = new([PublicKey(newKey), PublicKey(oldKey)]);

            foreach (SendGridWebhookVerifier verifier in new[] { oldFirst, newFirst })
            {
                Assert.True(verifier.Verify(Body, Timestamp, Sign(oldKey, Timestamp, Body)));
                Assert.True(verifier.Verify(Body, Timestamp, Sign(newKey, Timestamp, Body)));
            }
        }

        [Fact]
        public void Accepts_a_key_pasted_with_pem_armour_and_line_breaks()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string armoured = "-----BEGIN PUBLIC KEY-----\n"
                + string.Join('\n', Chunk(PublicKey(key), 64))
                + "\n-----END PUBLIC KEY-----\n";

            SendGridWebhookVerifier verifier = new([armoured]);

            Assert.True(verifier.Verify(Body, Timestamp, Sign(key, Timestamp, Body)));
        }

        [Fact]
        public void Refuses_to_be_constructed_without_a_usable_key()
        {
            Assert.Throws<ArgumentException>(() => new SendGridWebhookVerifier([]));
            Assert.Throws<ArgumentException>(() => new SendGridWebhookVerifier(["   "]));
            Assert.Throws<ArgumentException>(() => new SendGridWebhookVerifier(["not base64!"]));
            Assert.Throws<ArgumentException>(() => new SendGridWebhookVerifier([Convert.ToBase64String([1, 2, 3])]));
        }

        private static string PublicKey(ECDsa key)
        {
            return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        }

        private static string Sign(ECDsa key, string timestamp, byte[] body)
        {
            byte[] payload = [.. Encoding.UTF8.GetBytes(timestamp), .. body];
            return Convert.ToBase64String(key.SignData(
                payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        }

        private static IEnumerable<string> Chunk(string value, int size)
        {
            for (int i = 0; i < value.Length; i += size)
            {
                yield return value.Substring(i, Math.Min(size, value.Length - i));
            }
        }
    }
}
