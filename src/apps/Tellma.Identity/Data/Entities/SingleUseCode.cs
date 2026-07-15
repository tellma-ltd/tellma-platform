// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>What a single-use secret is issued for; verification is purpose-bound.</summary>
    public enum SingleUseCodePurpose
    {
        /// <summary>An email one-time code used as the primary sign-in factor.</summary>
        SignIn = 0,

        /// <summary>An email one-time code used as a second factor.</summary>
        SecondFactor = 1,

        /// <summary>An email one-time code used to raise assurance during step-up.</summary>
        StepUp = 2,

        /// <summary>An email one-time code starting credential recovery.</summary>
        Recovery = 3,

        /// <summary>A single-use invitation link token (the email-ownership proof).</summary>
        Invitation = 4,

        /// <summary>A single-use password-reset link token.</summary>
        PasswordReset = 5,
    }

    /// <summary>
    ///     A single-use secret: email one-time codes and one-time link tokens (invitation,
    ///     password reset). Only a SHA-256 hash is stored; consumption is a conditional update so
    ///     concurrent double-submission has exactly one winner. The built-in TOTP-based providers
    ///     are replayable within their window, which is why this store exists.
    /// </summary>
    public sealed class SingleUseCode
    {
        /// <summary>The row id, carried inside link tokens for O(1) lookup.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The user the secret was issued to.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>The purpose the secret is valid for.</summary>
        public SingleUseCodePurpose Purpose { get; set; }

        /// <summary>Base64 SHA-256 hash of the code or link secret; the clear value is never stored.</summary>
        public string SecretHash { get; set; } = string.Empty;

        /// <summary>
        ///     The browser flow the secret is bound to (the login-flow cookie id), so a code
        ///     phished into a different session fails verification. Null for link tokens, which
        ///     are their own possession proof.
        /// </summary>
        public string? FlowBinding { get; set; }

        /// <summary>
        ///     The validated post-completion destination for invitation tokens, stored
        ///     server-side so the emailed link cannot be tampered into an open redirect.
        /// </summary>
        public string? ReturnUrl { get; set; }

        /// <summary>The API client that requested issuance, when issued machine-to-machine.</summary>
        public string? CreatedByClientId { get; set; }

        /// <summary>When the secret was issued.</summary>
        public DateTimeOffset CreatedUtc { get; set; }

        /// <summary>When the secret expires.</summary>
        public DateTimeOffset ExpiresUtc { get; set; }

        /// <summary>When the secret was consumed; a consumed secret never verifies again.</summary>
        public DateTimeOffset? ConsumedUtc { get; set; }

        /// <summary>Failed verification attempts; the secret is invalidated past the maximum.</summary>
        public int Attempts { get; set; }
    }
}
