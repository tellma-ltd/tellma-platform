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
    ///     How far the email carrying a secret has got. Values are explicit and permanent: they are
    ///     persisted, so renumbering them would silently reinterpret existing rows.
    /// </summary>
    public enum EmailDispatchState
    {
        /// <summary>Not yet handed to a transport. The only state the recovery sweep claims.</summary>
        Pending = 0,

        /// <summary>Accepted by a transport for real delivery.</summary>
        Sent = 1,

        /// <summary>Handled by the sandbox policy; success-class and terminal, but nothing went out.</summary>
        Sandboxed = 2,

        /// <summary>Refused permanently (an undeliverable address). Never retried.</summary>
        Rejected = 3,

        /// <summary>Retried until the attempt cap without ever being accepted.</summary>
        Abandoned = 4,
    }

    /// <summary>
    ///     What a provider last reported about an email's delivery. Explicit and permanent for the
    ///     same reason as <see cref="EmailDispatchState" />; deliberately the engine's own enum
    ///     rather than the platform contract's, so a reordering upstream cannot reinterpret stored
    ///     rows.
    /// </summary>
    public enum EmailDeliveryStatus
    {
        /// <summary>Temporarily delayed at the provider; the only non-terminal report.</summary>
        Deferred = 1,

        /// <summary>A report the platform could not classify; the provider's own name carries it.</summary>
        Other = 2,

        /// <summary>Accepted by the recipient's mail server.</summary>
        Delivered = 3,

        /// <summary>Withheld by the provider (a suppression list, a prior bounce).</summary>
        Dropped = 4,

        /// <summary>Delivery failed for a reason the provider did not classify as a bounce.</summary>
        Failed = 5,

        /// <summary>Rejected by the recipient's mail server.</summary>
        Bounced = 6,

        /// <summary>The recipient marked the message as spam — the most actionable report there is.</summary>
        SpamReported = 7,
    }

    /// <summary>
    ///     A single-use secret: email one-time codes and one-time link tokens (invitation,
    ///     password reset). Only a SHA-256 hash is stored; consumption is a conditional update so
    ///     concurrent double-submission has exactly one winner. The built-in TOTP-based providers
    ///     are replayable within their window, which is why this store exists.
    ///     <para>
    ///         The row also records what became of the email carrying the secret. That is what lets
    ///         an invitation survive a crash between issuing the token and putting it on the wire —
    ///         the one message whose recipient is not waiting for it and so cannot ask again.
    ///     </para>
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

        /// <summary>How far the email carrying this secret has got.</summary>
        public EmailDispatchState DispatchState { get; set; }

        /// <summary>
        ///     When the secret's current link or code went onto the wire; null until it has. The
        ///     secret rotates when the sweep resends, so this timestamps the link a recipient
        ///     actually holds, not the row's creation.
        /// </summary>
        public DateTimeOffset? SentUtc { get; set; }

        /// <summary>
        ///     How long the sweep instance that claimed this row holds it. A lease rather than a
        ///     flag, so an instance that dies mid-send releases the row by expiry instead of
        ///     stranding it forever.
        /// </summary>
        public DateTimeOffset? DispatchClaimedUntil { get; set; }

        /// <summary>
        ///     Sends that failed in a way worth retrying. Only a transient failure counts: a
        ///     permanent rejection is terminal on its first occurrence and never consumes one.
        /// </summary>
        public int DispatchAttempts { get; set; }

        /// <summary>The transport's own id for the accepted message; support lookups only.</summary>
        public string? ProviderMessageId { get; set; }

        /// <summary>
        ///     Whether delivery events may still arrive for this message. False makes
        ///     <see cref="EmailDispatchState.Sent" /> the terminal state — an on-premise SMTP relay
        ///     reports nothing back, and silence there means "no feedback", never "not delivered".
        /// </summary>
        public bool ExpectsDeliveryEvents { get; set; }

        /// <summary>What the provider last reported; null until it reports anything.</summary>
        public EmailDeliveryStatus? DeliveryStatus { get; set; }

        /// <summary>When <see cref="DeliveryStatus" /> last changed.</summary>
        public DateTimeOffset? DeliveryUpdatedUtc { get; set; }

        /// <summary>The provider's reason for a failure, truncated; null for a success.</summary>
        public string? DeliveryReason { get; set; }

        /// <summary>
        ///     The provider's id for the event last applied. Providers deliver events at least
        ///     once, so this is what keeps a redelivery from being applied twice.
        /// </summary>
        public string? LastProviderEventId { get; set; }
    }
}
