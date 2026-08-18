// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Tokens
{
    /// <summary>The shape of a clear one-time token, in the one place that decides it.</summary>
    /// <remarks>
    ///     A token is <c>{id}.{secret}</c>: the row id travels in the open so redemption is a
    ///     single indexed lookup rather than a scan, and only the secret half is ever compared.
    ///     The id is not sensitive — it identifies a row, and the hash beside it is what proves
    ///     possession — which is also what makes it the right value to correlate the mail by.
    /// </remarks>
    public static class OneTimeTokenFormat
    {
        /// <summary>The row id a clear token names.</summary>
        /// <param name="token">A token as returned by issuance.</param>
        /// <returns>The row id, or the whole value when it carries no separator.</returns>
        public static string IdOf(string token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);

            int separator = token.IndexOf('.', StringComparison.Ordinal);
            return separator < 0 ? token : token[..separator];
        }
    }
}
