// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     The engine's identity in the platform's delivery-event routing: the owner key it stamps
    ///     on outgoing mail and answers events for.
    /// </summary>
    /// <remarks>
    ///     The reference is the single-use code's row id and nothing else. That is not a stylistic
    ///     choice — a correlation transits the mail provider and comes back on an endpoint guarded
    ///     only by signature verification, so the contract requires it to be meaningless outside
    ///     the platform. An address or a user id there would be a disclosure; a row key is not.
    /// </remarks>
    internal static class IdentityEmailCorrelation
    {
        /// <summary>The owner key, unique across the composition and validated at startup.</summary>
        internal const string OwnerKey = "identity";

        /// <summary>Mints the correlation for the mail carrying one single-use secret.</summary>
        /// <param name="singleUseCodeId">The row the events should find.</param>
        /// <returns>The correlation to attach at send time.</returns>
        internal static EmailCorrelation For(string singleUseCodeId)
        {
            // No tenant: identity's state is unsharded, which is exactly the case the contract's
            // null TenantId describes.
            return new EmailCorrelation(OwnerKey, singleUseCodeId);
        }

        /// <summary>The row a message's correlation points at, or null when it is not ours.</summary>
        /// <param name="message">The message that was sent.</param>
        /// <returns>The single-use code's row id, or null.</returns>
        internal static string? ReferenceOf(EmailMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            return message.Correlation is { } correlation
                && string.Equals(correlation.OwnerKey, OwnerKey, StringComparison.Ordinal)
                ? correlation.Reference
                : null;
        }
    }
}
