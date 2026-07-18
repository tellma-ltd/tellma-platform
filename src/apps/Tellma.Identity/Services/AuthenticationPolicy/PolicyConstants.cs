// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>
    ///     The fixed authentication-method vocabulary used by the <c>tellma_allowed_methods</c>
    ///     allow-list and the concrete-methods claim. Distributions select from this catalog;
    ///     they cannot extend it.
    /// </summary>
    public static class AuthenticationMethods
    {
        /// <summary>Passkey (WebAuthn/FIDO2), including roaming hardware security keys.</summary>
        public const string Passkey = "passkey";

        /// <summary>Single-use email one-time code.</summary>
        public const string EmailCode = "email_code";

        /// <summary>Authenticator-app TOTP second factor.</summary>
        public const string Totp = "totp";

        /// <summary>Password (offered only when a distribution's policy enables it).</summary>
        public const string Password = "password";

        /// <summary>Google external login.</summary>
        public const string Google = "google";

        /// <summary>Microsoft external login.</summary>
        public const string Microsoft = "microsoft";

        /// <summary>The complete vocabulary, in catalog order.</summary>
        public static readonly IReadOnlyList<string> All = [Passkey, EmailCode, Totp, Password, Google, Microsoft];
    }

    /// <summary>
    ///     The private authentication-context tier scheme carried in <c>acr</c>, corresponding to
    ///     NIST SP 800-63B authenticator assurance levels, plus the OpenID Connect EAP ACR input
    ///     values the server accepts as synonyms.
    /// </summary>
    public static class AcrTiers
    {
        /// <summary>Single factor (baseline).</summary>
        public const string Aal1 = "urn:tellma:acr:aal1";

        /// <summary>Two distinct factors, or one multi-factor authenticator (a passkey satisfies this).</summary>
        public const string Aal2 = "urn:tellma:acr:aal2";

        /// <summary>
        ///     Phishing-resistant and device-bound (non-synced). A fully substantiated NIST AAL3
        ///     additionally requires attestation, which is deferred.
        /// </summary>
        public const string Aal3 = "urn:tellma:acr:aal3";

        /// <summary>EAP "phishing-resistant" input; maps to the passkey tier (<see cref="Aal2" />).</summary>
        public const string PhishingResistant = "phr";

        /// <summary>EAP "phishing-resistant hardware" input; maps to <see cref="Aal3" />.</summary>
        public const string PhishingResistantHardware = "phrh";
    }

    /// <summary>Tellma-defined claim names (standard claims come from OpenIddict's constants).</summary>
    public static class TellmaClaims
    {
        /// <summary>The session identifier binding back-channel logout to the SSO session.</summary>
        public const string Sid = "sid";

        /// <summary>
        ///     The concrete authentication methods used, in the allow-list vocabulary — the
        ///     purpose-built set-membership claim for resource servers that must enforce methods,
        ///     since RFC 8176 <c>amr</c> values are deliberately coarser.
        /// </summary>
        public const string Methods = "tellma_methods";

        /// <summary>
        ///     The allow-list under which tokens were issued; private server-side state (never
        ///     serialized into tokens) used to re-enforce the list at refresh time.
        /// </summary>
        public const string AllowedMethods = "tellma_allowed_methods";
    }

    /// <summary>Tellma-defined OAuth request parameters.</summary>
    public static class TellmaParameters
    {
        /// <summary>
        ///     The space-delimited method allow-list a distribution sends inside the pushed
        ///     authorization request; no standard OIDC equivalent exists.
        /// </summary>
        public const string AllowedMethods = "tellma_allowed_methods";
    }

    /// <summary>
    ///     Claims recorded on the SSO session cookie by the engine's sign-in service (internal
    ///     session state; never emitted into tokens directly).
    /// </summary>
    public static class SignInClaims
    {
        /// <summary>
        ///     Whether the passkey used is device-bound (non-synced), per the authenticator's
        ///     self-asserted backup-eligibility flag.
        /// </summary>
        public const string PasskeyDeviceBound = "tellma_passkey_device_bound";
    }
}
