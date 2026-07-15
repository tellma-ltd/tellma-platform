// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Keys
{
    /// <summary>What a loaded certificate is used for.</summary>
    public enum CertificateUse
    {
        /// <summary>Token signing; the public half is published in JWKS.</summary>
        Signing = 0,

        /// <summary>Token encryption; never published.</summary>
        Encryption = 1,
    }

    /// <summary>
    ///     Loads X.509 certificates (with private keys) from one kind of source. Sources return
    ///     every valid certificate they hold so overlap rotation works: OpenIddict signs with the
    ///     certificate expiring furthest in the future and publishes all of them for validation.
    /// </summary>
    public interface ICertificateSource
    {
        /// <summary>Loads the certificates for one slot.</summary>
        /// <param name="options">The slot's source configuration.</param>
        /// <param name="use">Whether the certificates sign or encrypt tokens.</param>
        /// <returns>The loaded certificates; never empty (sources throw when nothing loads).</returns>
        IReadOnlyList<X509Certificate2> Load(TellmaIdentityCertificateOptions options, CertificateUse use);
    }
}
