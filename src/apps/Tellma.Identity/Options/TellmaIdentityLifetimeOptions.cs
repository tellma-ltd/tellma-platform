// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>Token lifetimes issued by the authority.</summary>
    public sealed class TellmaIdentityLifetimeOptions
    {
        /// <summary>
        ///     Access-token lifetime. Deliberately short: expiry is also the point at which the
        ///     authority re-evaluates authentication policy on refresh.
        /// </summary>
        public TimeSpan AccessToken { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>Identity-token lifetime; the token is consumed once at login.</summary>
        public TimeSpan IdentityToken { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        ///     The sliding idle lifetime of refresh tokens. Rotation is on: each refresh issues a
        ///     new token valid for this window from last use, so an idle session expires this long
        ///     after its last refresh.
        /// </summary>
        public TimeSpan RefreshTokenIdle { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        ///     How long an already-rotated refresh token remains redeemable, so a legitimate
        ///     concurrent/multi-tab refresh race is not treated as replay and does not revoke the
        ///     token family.
        /// </summary>
        public TimeSpan RefreshTokenReuseLeeway { get; set; } = TimeSpan.FromSeconds(30);
    }
}
