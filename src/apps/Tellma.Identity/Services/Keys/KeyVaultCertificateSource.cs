// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Keys
{
    /// <summary>
    ///     Azure Key Vault certificates via managed identity (a config-gated optional path — the
    ///     server runs fully on-prem without it). Private keys are retrieved through the vault's
    ///     secrets endpoint, so the identity needs <c>secrets/get</c> in addition to
    ///     <c>certificates/get</c>/<c>list</c>. When configured to include all enabled versions,
    ///     every non-expired version is loaded — the newest signs while older ones stay published
    ///     for validation — making overlap rotation a pure Key Vault operation.
    /// </summary>
    public sealed class KeyVaultCertificateSource : ICertificateSource
    {
        /// <inheritdoc />
        public IReadOnlyList<X509Certificate2> Load(TellmaIdentityCertificateOptions options, CertificateUse use)
        {
            ArgumentNullException.ThrowIfNull(options);

            Uri vaultUri = options.KeyVault.VaultUri
                ?? throw new InvalidOperationException("TellmaIdentity Key Vault certificate source requires a VaultUri.");
            string name = options.KeyVault.CertificateName
                ?? throw new InvalidOperationException("TellmaIdentity Key Vault certificate source requires a CertificateName.");

            TokenCredential credential = new DefaultAzureCredential();
            CertificateClient certificates = new(vaultUri, credential);
            SecretClient secrets = new(vaultUri, credential);

            List<X509Certificate2> loaded = [];
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (options.KeyVault.IncludeAllEnabledVersions)
            {
                foreach (CertificateProperties version in certificates.GetPropertiesOfCertificateVersions(name))
                {
                    // Skip disabled and expired versions; expired keys have no validation value.
                    if (version.Enabled != true || (version.ExpiresOn is { } expires && expires <= now))
                    {
                        continue;
                    }

                    loaded.Add(LoadVersion(secrets, name, version.Version));
                }
            }
            else
            {
                loaded.Add(LoadVersion(secrets, name, version: null));
            }

            return loaded.Count > 0
                ? loaded
                : throw new InvalidOperationException(
                    $"Key Vault certificate '{name}' has no enabled, non-expired version to load for {use}.");
        }

        /// <summary>Loads one certificate version, private key included, via the secrets endpoint.</summary>
        private static X509Certificate2 LoadVersion(SecretClient secrets, string name, string? version)
        {
            KeyVaultSecret secret = secrets.GetSecret(name, version).Value;

            // Key Vault exposes exportable certificates as base64 PKCS#12 secrets.
            byte[] pkcs12 = Convert.FromBase64String(secret.Value);
            return X509CertificateLoader.LoadPkcs12(pkcs12, password: null);
        }
    }
}
