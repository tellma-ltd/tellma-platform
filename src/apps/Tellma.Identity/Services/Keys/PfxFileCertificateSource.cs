// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Keys
{
    /// <summary>PKCS#12 (PFX) files on disk — the on-prem key-material path.</summary>
    public sealed class PfxFileCertificateSource : ICertificateSource
    {
        /// <inheritdoc />
        public IReadOnlyList<X509Certificate2> Load(TellmaIdentityCertificateOptions options, CertificateUse use)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<X509Certificate2> certificates = [];
            foreach (TellmaIdentityPfxFileOptions file in options.PfxFiles)
            {
                if (string.IsNullOrWhiteSpace(file.Path))
                {
                    continue;
                }

                certificates.Add(X509CertificateLoader.LoadPkcs12FromFile(file.Path, file.Password));
            }

            return certificates.Count > 0
                ? certificates
                : throw new InvalidOperationException($"No {use} certificate could be loaded from the configured PFX files.");
        }
    }
}
