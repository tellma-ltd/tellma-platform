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

        /// <summary>
        ///     Mints a fresh secret for an existing token row, keeping its identity and its
        ///     context, and returns the new clear token.
        /// </summary>
        /// <remarks>
        ///     For resending a link whose mail never went out. Only the hash is ever stored, so the
        ///     original link cannot be reproduced — the row is given a new secret instead of a new
        ///     row, which keeps one invitation to one record and needs no "superseded" state. The
        ///     previous link stops working the moment this returns, which is the intended reading:
        ///     if it had reached anyone, this resend would not be happening.
        /// </remarks>
        /// <param name="codeId">The row to rotate.</param>
        /// <param name="lifetime">How long the new secret stays valid, measured from now.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The new clear token, or null when the row is gone or already consumed.</returns>
        Task<string?> RotateAsync(string codeId, TimeSpan lifetime, CancellationToken cancellationToken);

        /// <summary>
        ///     Reports whether a token is currently usable, <em>without</em> consuming it.
        ///     <para>
        ///         For pages that land on a link and want to say "this link has expired" before
        ///         the user fills anything in. Redeeming to find that out would burn a good link
        ///         on arrival, which is why those pages could only ever fail on submit.
        ///     </para>
        ///     <para>
        ///         Advisory, not a guarantee: the token can still be consumed or expire between
        ///         the peek and the redeem, so the redeem remains the decision.
        ///     </para>
        /// </summary>
        /// <param name="token">The clear token string.</param>
        /// <param name="purpose">The purpose the token must have been issued for.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>Whether the token would redeem right now.</returns>
        Task<bool> PeekAsync(string token, SingleUseCodePurpose purpose, CancellationToken cancellationToken);

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
