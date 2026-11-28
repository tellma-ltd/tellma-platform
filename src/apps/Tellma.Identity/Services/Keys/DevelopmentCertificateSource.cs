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
    ///     cookies survive restarts. The options validator rejects this source both without the
    ///     explicit development flag and outside the Development environment, so it never loads in
    ///     production.
    /// </summary>
    /// <remarks>
    ///     Every read and every replacement of a persisted certificate holds an exclusive lock on a
    ///     gate file beside it, so hosts starting together, in one process or several, never read a
    ///     certificate while another replaces it.
    /// </remarks>
    public sealed class DevelopmentCertificateSource : ICertificateSource
    {
        /// <summary>Certificate validity; regenerated when less than 30 days remain.</summary>
        private static readonly TimeSpan Validity = TimeSpan.FromDays(365);

        /// <summary>How long a load waits for another holder of the gate before it gives up.</summary>
        private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The pause between two attempts to take the gate.</summary>
        private static readonly TimeSpan GateRetry = TimeSpan.FromMilliseconds(25);

        /// <summary>The folder the certificates persist in; null for the local application-data folder.</summary>
        private readonly string? _directory;

        /// <summary>Creates the source over the local application-data folder.</summary>
        public DevelopmentCertificateSource()
        {
        }

        /// <summary>Creates the source over a folder of the caller's.</summary>
        /// <param name="directory">The folder the certificates persist in.</param>
        internal DevelopmentCertificateSource(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            _directory = directory;
        }

        /// <inheritdoc />
        public IReadOnlyList<X509Certificate2> Load(TellmaIdentityCertificateOptions options, CertificateUse use)
        {
            ArgumentNullException.ThrowIfNull(options);

            string directory = _directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                "Tellma", "Identity", "dev-keys");
            string file = Path.Combine(directory, use == CertificateUse.Signing ? "dev-signing.pfx" : "dev-encryption.pfx");

            // --- The gate: one holder at a time reads or replaces the certificate ---
            FileStream gate;
            try
            {
                _ = Directory.CreateDirectory(directory);
                gate = AcquireGate(file + ".lock");
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only data folder (rare, e.g. locked-down CI): an in-memory certificate for
                // this process only.
                return [X509CertificateLoader.LoadPkcs12(Generate(use), password: null)];
            }

            using (gate)
            {
                // --- Reuse the persisted certificate while it has comfortable validity left ---
                if (File.Exists(file))
                {
                    X509Certificate2 existing = X509CertificateLoader.LoadPkcs12FromFile(file, password: null);
                    if (existing.NotAfter > DateTimeOffset.UtcNow.AddDays(30))
                    {
                        return [existing];
                    }

                    existing.Dispose();
                }

                // --- Generate and replace atomically, so a crash mid-write never leaves a torn file ---
                byte[] pkcs12 = Generate(use);
                string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, pkcs12);
                    File.Move(temporary, file, overwrite: true);
                }
                catch (UnauthorizedAccessException)
                {
                    // The folder takes the gate but not the certificate: this process's copy only.
                    File.Delete(temporary);
                    return [X509CertificateLoader.LoadPkcs12(pkcs12, password: null)];
                }

                return [X509CertificateLoader.LoadPkcs12FromFile(file, password: null)];
            }
        }

        /// <summary>
        ///     Takes the gate file exclusively, retrying while another thread or process holds it.
        /// </summary>
        /// <param name="path">The gate file's path.</param>
        /// <returns>The open gate, which releases the lock when disposed.</returns>
        /// <exception cref="IOException">The gate stayed taken for the whole timeout.</exception>
        private static FileStream AcquireGate(string path)
        {
            long deadline = Environment.TickCount64 + (long)GateTimeout.TotalMilliseconds;
            while (true)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (Environment.TickCount64 < deadline)
                {
                    // Another holder is reading or replacing the certificate; it releases within moments.
                    Thread.Sleep(GateRetry);
                }
            }
        }

        /// <summary>Generates a fresh self-signed certificate and exports it as PKCS#12.</summary>
        /// <param name="use">Whether the certificate signs or encrypts.</param>
        /// <returns>The PKCS#12 bytes, without a password.</returns>
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
