// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Keys
{
    /// <summary>
    ///     Development-only key material: self-signed certificates generated on first run and
    ///     persisted under the local application-data folder, so locally issued tokens and
    ///     cookies survive restarts. Never reachable in production — the options validator
    ///     rejects this source without the explicit development flag.
    /// </summary>
    public sealed class DevelopmentCertificateSource : ICertificateSource
    {
        /// <summary>Certificate validity; regenerated when less than 30 days remain.</summary>
        private static readonly TimeSpan Validity = TimeSpan.FromDays(365);

        /// <inheritdoc />
        public IReadOnlyList<X509Certificate2> Load(TellmaIdentityCertificateOptions options, CertificateUse use)
        {
            ArgumentNullException.ThrowIfNull(options);

            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                "Tellma", "Identity", "dev-keys");
            Directory.CreateDirectory(directory);

            string file = Path.Combine(directory, use == CertificateUse.Signing ? "dev-signing.pfx" : "dev-encryption.pfx");

            // Reuse the persisted certificate while it has comfortable validity left.
            if (File.Exists(file))
            {
                X509Certificate2 existing = X509CertificateLoader.LoadPkcs12FromFile(file, password: null);
                if (existing.NotAfter > DateTimeOffset.UtcNow.AddDays(30))
                {
                    return [existing];
                }

                existing.Dispose();
            }

            byte[] pkcs12 = Generate(use);
            try
            {
                // Atomic move so two processes generating concurrently cannot interleave writes;
                // the loser simply loads the winner's file below.
                string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, pkcs12);
                try
                {
                    File.Move(temporary, file, overwrite: false);
                }
                catch (IOException)
                {
                    File.Delete(temporary);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only data folder (rare, e.g. locked-down CI): fall back to the in-memory
                // certificate for this process only.
                return [X509CertificateLoader.LoadPkcs12(pkcs12, password: null)];
            }

            return [X509CertificateLoader.LoadPkcs12FromFile(file, password: null)];
        }

        /// <summary>Generates a fresh self-signed certificate and exports it as PKCS#12.</summary>
        private static byte[] Generate(CertificateUse use)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (use == CertificateUse.Signing)
            {
                // ES256 signing key.
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                CertificateRequest request = new("CN=Tellma Identity Dev Signing", key, HashAlgorithmName.SHA256);
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
                using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.Add(Validity));
                return certificate.Export(X509ContentType.Pkcs12);
            }
            else
            {
                // RSA-OAEP encryption key.
                using var key = RSA.Create(2048);
                CertificateRequest request = new("CN=Tellma Identity Dev Encryption", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, critical: true));
                using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.Add(Validity));
                return certificate.Export(X509ContentType.Pkcs12);
            }
        }
    }
}
