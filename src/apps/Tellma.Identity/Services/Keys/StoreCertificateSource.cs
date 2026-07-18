// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Keys
{
    /// <summary>
    ///     The current user's certificate store, looked up by thumbprint. On Azure App Service
    ///     this pairs with <c>WEBSITE_LOAD_CERTIFICATES</c>, which loads Key-Vault-synced
    ///     certificates into <c>CurrentUser\My</c>; it works the same for certificates installed
    ///     on-prem.
    /// </summary>
    public sealed class StoreCertificateSource : ICertificateSource
    {
        /// <inheritdoc />
        public IReadOnlyList<X509Certificate2> Load(TellmaIdentityCertificateOptions options, CertificateUse use)
        {
            ArgumentNullException.ThrowIfNull(options);

            using X509Store store = new(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            List<X509Certificate2> certificates = [];
            foreach (string thumbprint in options.StoreThumbprints)
            {
                if (string.IsNullOrWhiteSpace(thumbprint))
                {
                    continue;
                }

                X509Certificate2Collection matches = store.Certificates.Find(
                    X509FindType.FindByThumbprint, thumbprint.Trim(), validOnly: false);
                if (matches.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"No certificate with thumbprint '{thumbprint}' was found in the CurrentUser/My store for {use}.");
                }

                certificates.AddRange(matches.Cast<X509Certificate2>());
            }

            return certificates.Count > 0
                ? certificates
                : throw new InvalidOperationException($"No {use} certificate thumbprints were configured.");
        }
    }
}
