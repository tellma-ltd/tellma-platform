// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Identity
{
    /// <summary>
    ///     A service account the identity server registered: its client id and the secret it answers
    ///     exactly once. Losing the secret means deleting the account and creating another.
    /// </summary>
    /// <remarks>
    ///     The secret never appears in the record's text form, so a log line or an assertion message
    ///     that prints the record never carries it.
    /// </remarks>
    /// <param name="ClientId">The account's client id at the identity server.</param>
    /// <param name="ClientSecret">The account's secret, answered once.</param>
    public sealed record IdentityServiceAccount(string ClientId, string ClientSecret)
    {
        /// <summary>The record's text form, with the secret redacted.</summary>
        /// <returns>The client id and a placeholder for the secret.</returns>
        public override string ToString()
        {
            return $"{nameof(IdentityServiceAccount)} {{ {nameof(ClientId)} = {ClientId}, {nameof(ClientSecret)} = *** }}";
        }
    }
}
