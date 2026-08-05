// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Deployment-facing choices about the sign-in UI itself: which languages it offers, and
    ///     the legal documents it links to.
    /// </summary>
    public sealed class TellmaIdentityUiOptions
    {
        /// <summary>
        ///     The languages the sign-in UI offers, as culture names (<c>en</c>, <c>ar</c>). Empty
        ///     means every language the engine ships resources for, which is the hosted default.
        ///     An on-premise deployment that serves one region narrows the list rather than
        ///     presenting users a choice its own content does not support; a name the engine has
        ///     no resources for is rejected at startup rather than silently offering English.
        /// </summary>
        public IList<string> Languages { get; } = [];

        /// <summary>
        ///     Absolute URL of the privacy policy. The link appears only when this is set: a
        ///     deployment with no published policy shows nothing rather than a dead link.
        /// </summary>
        public string? PrivacyPolicyUrl { get; set; }

        /// <summary>
        ///     Absolute URL of the terms of service, linked on the same terms as
        ///     <see cref="PrivacyPolicyUrl" />.
        /// </summary>
        public string? TermsOfServiceUrl { get; set; }
    }
}
