// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Mutual-TLS (RFC 8705) opt-in for confidential machine clients that want
    ///     sender-constrained (certificate-bound) tokens. Off by default; enabling it also
    ///     requires the host to negotiate client certificates at the TLS layer.
    /// </summary>
    public sealed class TellmaIdentityMutualTlsOptions
    {
        /// <summary>Master switch; when false no mTLS feature is registered.</summary>
        public bool Enabled { get; set; }

        /// <summary>
        ///     Accept self-signed client certificates (the certificate acts as a deployment-bound
        ///     credential). This is the recommended mode for service accounts.
        /// </summary>
        public bool AcceptSelfSignedClientCertificates { get; set; } = true;

        /// <summary>
        ///     PEM/DER files of certificate authorities trusted for PKI client-certificate
        ///     authentication. When empty, PKI-based client authentication is not enabled.
        /// </summary>
        public IList<string> CertificateAuthorityPaths { get; } = [];

        /// <summary>Bind issued access tokens to the client certificate (<c>cnf</c> claim).</summary>
        public bool BindAccessTokens { get; set; } = true;

        /// <summary>Bind issued refresh tokens to the client certificate.</summary>
        public bool BindRefreshTokens { get; set; } = true;
    }
}
