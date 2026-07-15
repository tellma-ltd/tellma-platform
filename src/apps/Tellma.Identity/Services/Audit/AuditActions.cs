// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Audit
{
    /// <summary>The fixed catalog of audited actions.</summary>
    public static class AuditActions
    {
        /// <summary>An interactive sign-in succeeded.</summary>
        public const string LoginSucceeded = "LoginSucceeded";

        /// <summary>An interactive sign-in failed.</summary>
        public const string LoginFailed = "LoginFailed";

        /// <summary>A second-factor challenge succeeded.</summary>
        public const string TwoFactorSucceeded = "TwoFactorSucceeded";

        /// <summary>A second-factor challenge failed.</summary>
        public const string TwoFactorFailed = "TwoFactorFailed";

        /// <summary>A step-up re-authentication completed.</summary>
        public const string StepUpCompleted = "StepUpCompleted";

        /// <summary>An email one-time code was issued.</summary>
        public const string EmailCodeIssued = "EmailCodeIssued";

        /// <summary>An email one-time code verified successfully.</summary>
        public const string EmailCodeVerified = "EmailCodeVerified";

        /// <summary>An email one-time code failed verification.</summary>
        public const string EmailCodeFailed = "EmailCodeFailed";

        /// <summary>Email-code issuance was rate limited.</summary>
        public const string EmailCodeRateLimited = "EmailCodeRateLimited";

        /// <summary>A passkey was enrolled.</summary>
        public const string PasskeyEnrolled = "PasskeyEnrolled";

        /// <summary>A passkey was renamed.</summary>
        public const string PasskeyRenamed = "PasskeyRenamed";

        /// <summary>A passkey was removed.</summary>
        public const string PasskeyRemoved = "PasskeyRemoved";

        /// <summary>An authenticator (TOTP) second factor was enabled.</summary>
        public const string TotpEnabled = "TotpEnabled";

        /// <summary>The authenticator (TOTP) second factor was disabled.</summary>
        public const string TotpDisabled = "TotpDisabled";

        /// <summary>Recovery codes were (re)generated.</summary>
        public const string RecoveryCodesGenerated = "RecoveryCodesGenerated";

        /// <summary>A recovery code was used to sign in.</summary>
        public const string RecoveryCodeUsed = "RecoveryCodeUsed";

        /// <summary>An external login was linked to a local account.</summary>
        public const string ExternalLoginLinked = "ExternalLoginLinked";

        /// <summary>An external login was removed from a local account.</summary>
        public const string ExternalLoginRemoved = "ExternalLoginRemoved";

        /// <summary>Tokens were issued.</summary>
        public const string TokenIssued = "TokenIssued";

        /// <summary>A token request was rejected.</summary>
        public const string TokenRequestRejected = "TokenRequestRejected";

        /// <summary>Refresh-token reuse (replay) was detected.</summary>
        public const string RefreshReuseDetected = "RefreshReuseDetected";

        /// <summary>A token or grant was revoked.</summary>
        public const string TokenRevoked = "TokenRevoked";

        /// <summary>An SSO session was established.</summary>
        public const string SessionCreated = "SessionCreated";

        /// <summary>An SSO session was terminated.</summary>
        public const string SessionTerminated = "SessionTerminated";

        /// <summary>The user signed out everywhere (all sessions revoked).</summary>
        public const string SignOutEverywhere = "SignOutEverywhere";

        /// <summary>A back-channel logout token was delivered to a client.</summary>
        public const string BackchannelLogoutSent = "BackchannelLogoutSent";

        /// <summary>A back-channel logout delivery failed after retries.</summary>
        public const string BackchannelLogoutFailed = "BackchannelLogoutFailed";

        /// <summary>A user was invited.</summary>
        public const string UserInvited = "UserInvited";

        /// <summary>An invitation was accepted.</summary>
        public const string InvitationAccepted = "InvitationAccepted";

        /// <summary>A service account was created.</summary>
        public const string ServiceAccountCreated = "ServiceAccountCreated";

        /// <summary>A service account was deleted.</summary>
        public const string ServiceAccountDeleted = "ServiceAccountDeleted";

        /// <summary>A service account's secret was regenerated.</summary>
        public const string ServiceAccountSecretRegenerated = "ServiceAccountSecretRegenerated";

        /// <summary>An OAuth client was created or provisioned.</summary>
        public const string ClientCreated = "ClientCreated";

        /// <summary>An OAuth client registration was updated.</summary>
        public const string ClientUpdated = "ClientUpdated";

        /// <summary>An OAuth client registration was deleted.</summary>
        public const string ClientDeleted = "ClientDeleted";

        /// <summary>A temporary access pass was issued by an operator.</summary>
        public const string TapIssued = "TapIssued";

        /// <summary>A temporary access pass was used.</summary>
        public const string TapUsed = "TapUsed";

        /// <summary>A temporary access pass failed verification.</summary>
        public const string TapFailed = "TapFailed";

        /// <summary>A password was set or changed.</summary>
        public const string PasswordChanged = "PasswordChanged";

        /// <summary>A password reset was requested.</summary>
        public const string PasswordResetRequested = "PasswordResetRequested";

        /// <summary>A password was reset.</summary>
        public const string PasswordReset = "PasswordReset";

        /// <summary>A user's lifecycle state changed.</summary>
        public const string UserLifecycleChanged = "UserLifecycleChanged";

        /// <summary>A device-flow user code was approved.</summary>
        public const string DeviceCodeApproved = "DeviceCodeApproved";

        /// <summary>A device-flow user code was denied.</summary>
        public const string DeviceCodeDenied = "DeviceCodeDenied";

        /// <summary>Consent was granted to a third-party client.</summary>
        public const string ConsentGranted = "ConsentGranted";

        /// <summary>Consent was denied to a third-party client.</summary>
        public const string ConsentDenied = "ConsentDenied";

        /// <summary>The development admin identity was seeded.</summary>
        public const string DevAdminSeeded = "DevAdminSeeded";

        /// <summary>The break-glass administrator was seeded.</summary>
        public const string BootstrapAdminSeeded = "BootstrapAdminSeeded";

        /// <summary>The one-time setup token was used to bootstrap the break-glass administrator.</summary>
        public const string SetupTokenUsed = "SetupTokenUsed";

        /// <summary>A setup-token attempt failed.</summary>
        public const string SetupTokenFailed = "SetupTokenFailed";
    }
}
