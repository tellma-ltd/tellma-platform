// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.EmailCodes
{
    /// <summary>The outcome of verifying an email one-time code.</summary>
    public enum EmailCodeVerificationResult
    {
        /// <summary>The code matched and was consumed; it can never verify again.</summary>
        Success = 0,

        /// <summary>No outstanding code, wrong code, or a foreign browser session.</summary>
        Invalid = 1,

        /// <summary>The code exists but its ten-minute lifetime elapsed.</summary>
        Expired = 2,

        /// <summary>The code was invalidated after too many failed attempts.</summary>
        TooManyAttempts = 3,
    }

    /// <summary>
    ///     The custom email one-time-code provider: single-use, short-lived (10 minutes),
    ///     rate-limited, and bound to the requesting browser session — everything the built-in
    ///     TOTP-based email provider (replayable within its multi-minute window) is not.
    /// </summary>
    public interface IEmailCodeService
    {
        /// <summary>
        ///     Requests a code for an email address: rate-limits by IP first (so probing is bounded
        ///     regardless of whether the address exists), then — only for an active user — issues a
        ///     fresh code and hands it to the background mail worker. Returns without waiting for
        ///     delivery and does the same work-shape whether or not the account exists, so neither
        ///     the response nor its latency reveals account existence.
        /// </summary>
        /// <param name="email">The address a code was requested for.</param>
        /// <param name="purpose">What the code is for; verification is purpose-bound.</param>
        /// <param name="flowBinding">The requesting browser session's flow id.</param>
        /// <param name="ipAddress">The requester's IP, for rate limiting.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        Task RequestCodeAsync(
            string email,
            SingleUseCodePurpose purpose,
            string flowBinding,
            string? ipAddress,
            CancellationToken cancellationToken);

        /// <summary>Verifies and consumes a code (a conditional update — one winner under races).</summary>
        /// <param name="user">The user the code was issued to.</param>
        /// <param name="purpose">The purpose the code must have been issued for.</param>
        /// <param name="flowBinding">The verifying browser session's flow id.</param>
        /// <param name="code">The submitted code.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The verification outcome.</returns>
        Task<EmailCodeVerificationResult> VerifyAsync(
            TellmaIdentityUser user,
            SingleUseCodePurpose purpose,
            string? flowBinding,
            string code,
            CancellationToken cancellationToken);
    }
}
