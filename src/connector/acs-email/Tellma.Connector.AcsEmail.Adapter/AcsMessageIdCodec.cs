// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>
    ///     Encodes a correlation into an internet message id and reads it back off a delivery report.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ACS keeps a customer-supplied message id and echoes it on every delivery report, which
    ///         makes the <c>Message-ID</c> header the correlation channel on this transport. The form
    ///         is <c>&lt;tlm1-{base32hex}-{entropy}@{domain}&gt;</c>:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>base32hex because the canonical correlation contains colons, which are not legal
    ///             in an RFC 5322 atom, and the value must survive byte for byte.</item>
    ///         <item>a random suffix per send, ignored at parse, because message ids must be unique
    ///             per message while correlations repeat across resends — and a repeated id risks
    ///             recipient-side threading and deduplication.</item>
    ///         <item>the sending domain on the right-hand side, because an id that disagrees with the
    ///             sending domain is a weak spam signal.</item>
    ///     </list>
    ///     <para>
    ///         Parsing is deliberately tolerant. An ACS-regenerated id, mail sent outside the
    ///         platform, and any future format all resolve to no correlation and flow through the
    ///         pipeline's uncorrelated metering rather than failing anything.
    ///     </para>
    /// </remarks>
    internal static class AcsMessageIdCodec
    {
        /// <summary>The format tag, which also leaves room for a future encoding.</summary>
        internal const string Tag = "tlm1";

        /// <summary>
        ///     The largest message id this codec will produce. RFC 5322 caps a header line at 998
        ///     characters, and the ACS-side limit is undocumented; a correlation is never worth a
        ///     rejected email, so an id over budget is simply not stamped.
        /// </summary>
        internal const int MaxMessageIdLength = 900;

        private const int EntropyByteCount = 5;

        /// <summary>Builds the message id for a correlated message.</summary>
        /// <param name="correlation">The correlation to encode.</param>
        /// <param name="sendingDomain">The domain part of the effective sender.</param>
        /// <param name="messageId">The angle-bracketed message id, or null when it would exceed
        ///     <see cref="MaxMessageIdLength" />.</param>
        /// <returns>True when an id was produced.</returns>
        internal static bool TryEncode(
            EmailCorrelation correlation, string sendingDomain, [NotNullWhen(true)] out string? messageId)
        {
            string payload = Base32Hex.Encode(Encoding.UTF8.GetBytes(correlation.ToString()));
            string entropy = Base32Hex.Encode(RandomNumberGenerator.GetBytes(EntropyByteCount));
            string candidate = $"<{Tag}-{payload}-{entropy}@{sendingDomain}>";

            if (candidate.Length > MaxMessageIdLength)
            {
                messageId = null;
                return false;
            }

            messageId = candidate;
            return true;
        }

        /// <summary>Reads a correlation back off an echoed internet message id.</summary>
        /// <param name="messageId">The <c>internetMessageId</c> a delivery report carried.</param>
        /// <param name="correlation">The decoded correlation, or null.</param>
        /// <returns>True when the id was one this codec produced and still parses.</returns>
        internal static bool TryParse(string? messageId, out EmailCorrelation? correlation)
        {
            correlation = null;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return false;
            }

            ReadOnlySpan<char> value = messageId.AsSpan().Trim();
            if (value.Length > 1 && value[0] == '<' && value[^1] == '>')
            {
                value = value[1..^1];
            }

            // A base32hex payload can never contain '@', so the last one is unambiguously the
            // domain separator even if the domain itself were odd.
            int at = value.LastIndexOf('@');
            ReadOnlySpan<char> local = at < 0 ? value : value[..at];

            int firstDash = local.IndexOf('-');
            if (firstDash < 0 || !local[..firstDash].SequenceEqual(Tag))
            {
                return false;
            }

            ReadOnlySpan<char> rest = local[(firstDash + 1)..];
            int lastDash = rest.LastIndexOf('-');
            return lastDash > 0
                && Base32Hex.TryDecode(rest[..lastDash], out byte[]? decoded)
                && EmailCorrelation.TryParse(Encoding.UTF8.GetString(decoded!), out correlation);
        }

        /// <summary>The domain part of an address, for the right-hand side of a message id.</summary>
        /// <param name="address">The sender address.</param>
        /// <returns>The lowercase domain, or "localhost" when the address has none.</returns>
        internal static string GetSendingDomain(string address)
        {
            int at = address.LastIndexOf('@');
            return at < 0 || at == address.Length - 1
                ? "localhost"
                : address[(at + 1)..].ToLowerInvariant();
        }
    }
}
