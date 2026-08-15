// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     Identifies the owner and subject of an email for delivery-event routing. Attached at send
    ///     time, round-tripped through the transport (e.g. provider custom arguments), and returned
    ///     on delivery events so each event finds the record it updates.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Canonical single-string form is <c>ownerKey:tenantId:reference</c>, with an empty
    ///         middle segment when <see cref="TenantId" /> is null. The first two colons delimit; the
    ///         remainder is the <see cref="Reference" />, which may therefore contain colons.
    ///         Contents must be meaningless outside the platform — row keys and tenant numbers only,
    ///         never user identifiers, addresses, or token material — because the value transits
    ///         third-party systems and returns on an endpoint protected only by signature
    ///         verification.
    ///     </para>
    ///     <para>
    ///         <see cref="OwnerKey" /> and <see cref="Reference" /> are get-only so that a
    ///         <c>with</c> expression cannot produce an unvalidated correlation: the constructor is
    ///         the only way to mint one, and it always validates.
    ///     </para>
    /// </remarks>
    /// <param name="OwnerKey">The event owner; matches <see cref="IEmailDeliveryEventHandler.OwnerKey" />.
    ///     Lowercase letters, digits, and hyphens; no colons; at most 32 characters.</param>
    /// <param name="Reference">Owner-opaque subject reference, typically a row id. Non-empty, at
    ///     most 128 characters.</param>
    /// <param name="TenantId">The tenant whose database holds the referenced state, when the owner's
    ///     state is tenant-sharded (the outbox); null for owners with unsharded state (identity).</param>
    public sealed record EmailCorrelation(string OwnerKey, string Reference, int? TenantId = null)
    {
        /// <summary>The inclusive upper bound on the length of <see cref="OwnerKey" />.</summary>
        public const int MaxOwnerKeyLength = 32;

        /// <summary>The inclusive upper bound on the length of <see cref="Reference" />.</summary>
        public const int MaxReferenceLength = 128;

        /// <inheritdoc cref="EmailCorrelation(string, string, int?)" />
        public string OwnerKey { get; } = ValidateOwnerKey(OwnerKey);

        /// <inheritdoc cref="EmailCorrelation(string, string, int?)" />
        public string Reference { get; } = ValidateReference(Reference);

        /// <summary>The canonical single-string form, for transports with one echo field.</summary>
        /// <returns>The correlation as <c>ownerKey:tenantId:reference</c>.</returns>
        public override string ToString()
        {
            // A null TenantId interpolates to the empty string, which is exactly the empty middle
            // segment the canonical form calls for. Invariant culture so a negative id never picks
            // up a culture-specific sign and the value round-trips byte for byte.
            return string.Create(CultureInfo.InvariantCulture, $"{OwnerKey}:{TenantId}:{Reference}");
        }

        /// <summary>Parses the canonical form; false when the value is null or malformed.</summary>
        /// <param name="value">The canonical form produced by <see cref="ToString" />.</param>
        /// <param name="result">The parsed correlation, or null when parsing failed.</param>
        /// <returns>True when <paramref name="value" /> was a well-formed canonical correlation.</returns>
        public static bool TryParse(string? value, [NotNullWhen(true)] out EmailCorrelation? result)
        {
            result = null;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            // Only the first two colons delimit; everything after the second one is the reference,
            // which is allowed to contain colons of its own.
            int firstColon = value.IndexOf(':');
            if (firstColon < 0)
            {
                return false;
            }

            int secondColon = value.IndexOf(':', firstColon + 1);
            if (secondColon < 0)
            {
                return false;
            }

            ReadOnlySpan<char> ownerKey = value.AsSpan(0, firstColon);
            ReadOnlySpan<char> tenant = value.AsSpan(firstColon + 1, secondColon - firstColon - 1);
            ReadOnlySpan<char> reference = value.AsSpan(secondColon + 1);

            // Accept exactly what ToString produces — the same rules the constructor enforces.
            if (!IsValidOwnerKey(ownerKey) || !IsValidReference(reference))
            {
                return false;
            }

            int? tenantId = null;
            if (!tenant.IsEmpty)
            {
                // AllowLeadingSign only: no whitespace, no thousands separators, no hex.
                if (!int.TryParse(tenant, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
                {
                    return false;
                }

                tenantId = parsed;
            }

            result = new EmailCorrelation(ownerKey.ToString(), reference.ToString(), tenantId);
            return true;
        }

        /// <summary>
        ///     Reports whether a value is shaped like an <see cref="OwnerKey" />: one or more
        ///     lowercase letters, digits, or hyphens, at most <see cref="MaxOwnerKeyLength" /> long.
        /// </summary>
        /// <param name="value">The candidate owner key.</param>
        /// <returns>True when the value is a well-formed owner key.</returns>
        public static bool IsValidOwnerKey(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty || value.Length > MaxOwnerKeyLength)
            {
                return false;
            }

            // Hand-rolled rather than a regex: the same check runs over spans during parsing, on
            // every inbound delivery event, and a character loop is both allocation-free and
            // obviously equivalent to ^[a-z0-9-]+$.
            foreach (char c in value)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidReference(ReadOnlySpan<char> value)
        {
            return !value.IsEmpty && !value.IsWhiteSpace() && value.Length <= MaxReferenceLength;
        }

        private static string ValidateOwnerKey(string ownerKey)
        {
            ArgumentNullException.ThrowIfNull(ownerKey);
            return IsValidOwnerKey(ownerKey)
                ? ownerKey
                : throw new ArgumentException(
                    $"An email correlation owner key must be 1 to {MaxOwnerKeyLength} lowercase letters, digits, or hyphens; received '{ownerKey}'.",
                    nameof(ownerKey));
        }

        private static string ValidateReference(string reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            return IsValidReference(reference)
                ? reference
                : throw new ArgumentException(
                    $"An email correlation reference must be non-blank and at most {MaxReferenceLength} characters; received a value of length {reference.Length}.",
                    nameof(reference));
        }
    }
}
