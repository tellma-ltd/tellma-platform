// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Claims;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>What became of a refresh token's redemption.</summary>
    internal enum RedemptionStatus
    {
        /// <summary>The authority issued new tokens.</summary>
        Redeemed,

        /// <summary>The authority refused the refresh token, or answered with tokens that do not hold up.</summary>
        Refused,

        /// <summary>The authority could not be reached, or failed on its side.</summary>
        Unreachable,
    }

    /// <summary>The answer to a refresh token's redemption.</summary>
    /// <param name="Status">What became of it.</param>
    /// <param name="Tokens">The new tokens, when redeemed.</param>
    /// <param name="Reason">Why it was refused or could not be done, for the log; never a token.</param>
    /// <param name="Failure">The failure, when the authority could not be reached.</param>
    internal sealed record Redemption(RedemptionStatus Status, RedeemedTokens? Tokens, string? Reason, Exception? Failure)
    {
        /// <summary>A redemption that issued tokens.</summary>
        /// <param name="tokens">The tokens.</param>
        /// <returns>The redemption.</returns>
        public static Redemption Redeemed(RedeemedTokens tokens)
        {
            return new Redemption(RedemptionStatus.Redeemed, tokens, null, null);
        }

        /// <summary>A redemption the authority refused.</summary>
        /// <param name="reason">Why.</param>
        /// <returns>The redemption.</returns>
        public static Redemption Refused(string reason)
        {
            return new Redemption(RedemptionStatus.Refused, null, reason, null);
        }

        /// <summary>A redemption that could not reach the authority.</summary>
        /// <param name="reason">Why.</param>
        /// <param name="failure">The failure, if one was thrown.</param>
        /// <returns>The redemption.</returns>
        public static Redemption Unreachable(string reason, Exception? failure = null)
        {
            return new Redemption(RedemptionStatus.Unreachable, null, reason, failure);
        }
    }

    /// <summary>The tokens a refresh issued.</summary>
    /// <param name="AccessToken">The new access token.</param>
    /// <param name="RefreshToken">The rotated refresh token; null when the authority kept the old one.</param>
    /// <param name="IdToken">The new identity token, as issued; null when the authority issued none.</param>
    /// <param name="IdTokenClaims">The new identity token's validated claims; null when it issued none.</param>
    /// <param name="TokenType">The access token's type.</param>
    /// <param name="ExpiresIn">How long the access token lives.</param>
    internal sealed record RedeemedTokens(
        string AccessToken,
        string? RefreshToken,
        string? IdToken,
        ClaimsIdentity? IdTokenClaims,
        string? TokenType,
        TimeSpan ExpiresIn);

    /// <summary>Redeems a session's refresh token at the authority's token endpoint.</summary>
    internal interface ITokenRedeemer
    {
        /// <summary>Redeems a refresh token.</summary>
        /// <param name="refreshToken">The refresh token.</param>
        /// <param name="cancellationToken">Cancels the redemption.</param>
        /// <returns>The answer; never throws for the authority's failures.</returns>
        Task<Redemption> RedeemAsync(string refreshToken, CancellationToken cancellationToken);
    }
}
