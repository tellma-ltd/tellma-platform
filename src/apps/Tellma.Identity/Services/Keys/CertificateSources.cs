// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Keys
{
    /// <summary>Resolves a certificate slot's configuration to its loaded certificates.</summary>
    public static class CertificateSources
    {
        /// <summary>Loads the certificates configured for one slot.</summary>
        /// <param name="options">The slot's source configuration.</param>
        /// <param name="use">Whether the certificates sign or encrypt tokens.</param>
        /// <returns>The loaded certificates.</returns>
        public static IReadOnlyList<X509Certificate2> Load(TellmaIdentityCertificateOptions options, CertificateUse use)
        {
            ArgumentNullException.ThrowIfNull(options);

            ICertificateSource source = options.Source switch
            {
                TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned => new DevelopmentCertificateSource(),
                TellmaIdentityCertificateSourceKind.PfxFile => new PfxFileCertificateSource(),
                TellmaIdentityCertificateSourceKind.CertificateStore => new StoreCertificateSource(),
                TellmaIdentityCertificateSourceKind.KeyVault => new KeyVaultCertificateSource(),
                TellmaIdentityCertificateSourceKind.None => throw new InvalidOperationException("No certificate source is configured."),
                _ => throw new InvalidOperationException("No certificate source is configured."),
            };

            return source.Load(options, use);
        }
    }
}
