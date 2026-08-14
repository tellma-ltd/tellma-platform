// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Tellma.Connector.SendGrid
{
    /// <summary>
    ///     Verifies the ECDSA signature SendGrid puts on every event-webhook delivery, using BCL
    ///     cryptography only.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The scheme: SHA-256 over the timestamp header immediately followed by the raw request
    ///         body, verified against a public key SendGrid issues as base64 SubjectPublicKeyInfo —
    ///         the curve rides in the key, so nothing here names one.
    ///     </para>
    ///     <para>
    ///         More than one key is accepted, which is the rotation affordance: SendGrid holds a
    ///         single signing key per webhook and rotating it regenerates that key, so the runbook is
    ///         regenerate, add the new key alongside the old, deploy, then drop the old.
    ///     </para>
    ///     <para>
    ///         There is deliberately no timestamp-freshness check. SendGrid redelivers failed batches
    ///         for up to 24 hours carrying the original event timestamps, a legitimate staleness no
    ///         tolerance window can distinguish from a replay; replay defence is handler-side
    ///         deduplication on the event id.
    ///     </para>
    /// </remarks>
    public sealed class SendGridWebhookVerifier
    {
        /// <summary>The header carrying the base64 DER signature.</summary>
        public const string SignatureHeaderName = "X-Twilio-Email-Event-Webhook-Signature";

        /// <summary>The header carrying the signed timestamp.</summary>
        public const string TimestampHeaderName = "X-Twilio-Email-Event-Webhook-Timestamp";

        private const string PemHeader = "-----BEGIN PUBLIC KEY-----";
        private const string PemFooter = "-----END PUBLIC KEY-----";

        private readonly byte[][] _publicKeys;

        /// <summary>Parses and validates the configured verification keys once.</summary>
        /// <param name="base64PublicKeys">The keys, as SendGrid presents them. PEM armour and
        ///     whitespace are tolerated, because operators paste them out of the dashboard.</param>
        /// <exception cref="ArgumentException">No key was supplied, or one of them is not a usable
        ///     public key — worth failing at startup rather than at three in the morning.</exception>
        public SendGridWebhookVerifier(IEnumerable<string> base64PublicKeys)
        {
            ArgumentNullException.ThrowIfNull(base64PublicKeys);

            List<byte[]> parsed = [];
            foreach (string key in base64PublicKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                byte[] spki = DecodeKey(key);
                try
                {
                    using var probe = ECDsa.Create();
                    probe.ImportSubjectPublicKeyInfo(spki, out _);
                }
                catch (CryptographicException exception)
                {
                    throw new ArgumentException(
                        "A SendGrid webhook verification key is not a valid public key.",
                        nameof(base64PublicKeys),
                        exception);
                }

                parsed.Add(spki);
            }

            _publicKeys = parsed.Count > 0
                ? [.. parsed]
                : throw new ArgumentException(
                    "At least one SendGrid webhook verification key is required.", nameof(base64PublicKeys));
        }

        /// <summary>Verifies a delivery against every configured key.</summary>
        /// <param name="body">The raw request body, byte for byte as it arrived.</param>
        /// <param name="timestamp">The timestamp header's value.</param>
        /// <param name="base64Signature">The signature header's value.</param>
        /// <returns>True when any configured key verifies the signature.</returns>
        public bool Verify(ReadOnlySpan<byte> body, string timestamp, string base64Signature)
        {
            if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(base64Signature))
            {
                return false;
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(base64Signature);
            }
            catch (FormatException)
            {
                return false;
            }

            int timestampByteCount = Encoding.UTF8.GetByteCount(timestamp);
            byte[] signedPayload = ArrayPool<byte>.Shared.Rent(timestampByteCount + body.Length);
            try
            {
                // The signed payload is the timestamp immediately followed by the body — no
                // separator, no re-serialization, and never a round-trip through a string.
                Encoding.UTF8.GetBytes(timestamp, signedPayload);
                body.CopyTo(signedPayload.AsSpan(timestampByteCount));
                ReadOnlySpan<byte> signed = signedPayload.AsSpan(0, timestampByteCount + body.Length);

                foreach (byte[] spki in _publicKeys)
                {
                    if (VerifyWithKey(spki, signed, signature))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                // Cleared on the way back: this buffer held the raw event body — recipient
                // addresses, bounce reasons, custom arguments — and the pool hands the very same
                // array to unrelated code next, beyond whatever that code writes into it.
                ArrayPool<byte>.Shared.Return(signedPayload, clearArray: true);
            }
        }

        private static bool VerifyWithKey(byte[] spki, ReadOnlySpan<byte> signed, ReadOnlySpan<byte> signature)
        {
            try
            {
                // Created per call: ECDsa is not documented as thread-safe and the webhook path is
                // concurrent, so only the parsed key bytes are cached.
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(spki, out _);

                // The signature format argument is mandatory. SendGrid sends an ASN.1 DER
                // SEQUENCE{r,s}, while the overload without it assumes the IEEE P1363 fixed-field
                // concatenation and would return false for every valid signature.
                return ecdsa.VerifyData(
                    signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (CryptographicException)
            {
                // One stale key among several is expected mid-rotation; keep trying the others.
                return false;
            }
        }

        private static byte[] DecodeKey(string key)
        {
            string trimmed = key.Trim();
            if (trimmed.StartsWith(PemHeader, StringComparison.Ordinal))
            {
                trimmed = trimmed
                    .Replace(PemHeader, string.Empty, StringComparison.Ordinal)
                    .Replace(PemFooter, string.Empty, StringComparison.Ordinal);
            }

            // Dashboard copy-paste routinely carries line breaks and stray spaces.
            trimmed = string.Concat(trimmed.Where(static c => !char.IsWhiteSpace(c)));

            try
            {
                return Convert.FromBase64String(trimmed);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException(
                    "A SendGrid webhook verification key is not valid base64.", nameof(key), exception);
            }
        }
    }
}
