// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Tokens
{
    /// <summary>A consumed one-time token and the context it carried.</summary>
    /// <param name="UserId">The user the token was issued to.</param>
    /// <param name="ReturnUrl">The validated post-completion destination, when any.</param>
    public sealed record OneTimeTokenContext(string UserId, string? ReturnUrl);

    /// <summary>
    ///     Issues and redeems single-use link tokens (invitations, password resets). Tokens are
    ///     stateful by necessity — single-use and supersede-on-reissue require a store — so a
    ///     stateless Data-Protection token is deliberately not used. Only a SHA-256 hash is stored;
    ///     consumption is a conditional update, so a token can be redeemed exactly once.
    /// </summary>
    public interface IOneTimeTokenService
    {
        /// <summary>Issues a token, superseding any outstanding token of the same purpose.</summary>
        /// <param name="userId">The user the token is for.</param>
        /// <param name="purpose">The token purpose (invitation, password reset).</param>
        /// <param name="lifetime">How long the token stays valid.</param>
        /// <param name="returnUrl">A validated post-completion destination stored server-side.</param>
        /// <param name="createdByClientId">The API client that requested issuance, for audit.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The clear token string (<c>{id}.{secret}</c>), returned only here.</returns>
        Task<string> IssueAsync(
            string userId,
            SingleUseCodePurpose purpose,
            TimeSpan lifetime,
            string? returnUrl,
            string? createdByClientId,
            CancellationToken cancellationToken);

        /// <summary>Redeems a token, consuming it so it can never be used again.</summary>
        /// <param name="token">The clear token string.</param>
        /// <param name="purpose">The purpose the token must have been issued for.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The token context on success, or null when invalid/expired/consumed.</returns>
        Task<OneTimeTokenContext?> RedeemAsync(
            string token,
            SingleUseCodePurpose purpose,
            CancellationToken cancellationToken);
    }
}
