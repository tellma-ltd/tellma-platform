// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Startup validation for <see cref="TellmaIdentityOptions" />. Every failure is reported
    ///     at once so a misconfigured deployment is fixed in one pass, and insecure combinations
    ///     (development keys outside development, HTTP without the explicit flag, an unencrypted
    ///     blob key ring) never boot.
    /// </summary>
    /// <param name="environment">
    ///     The host environment, when available: used to reject development-only settings outside
    ///     the Development environment regardless of their opt-in flag. Null for the eager
    ///     registration-time snapshot check, where the same rule is enforced later at start-up by
    ///     the DI-resolved validator that does have it.
    /// </param>
    public sealed class TellmaIdentityOptionsValidator(IHostEnvironment? environment = null)
        : IValidateOptions<TellmaIdentityOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, TellmaIdentityOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            // Issuer: required, absolute, HTTPS (outside the dev-insecure flag), and free of
            // query/fragment noise.
            if (options.Issuer is null)
            {
                failures.Add("TellmaIdentity:Issuer is required.");
            }
            else if (!options.Issuer.IsAbsoluteUri)
            {
                failures.Add("TellmaIdentity:Issuer must be an absolute URI.");
            }
            else if (!string.IsNullOrEmpty(options.Issuer.Query) || !string.IsNullOrEmpty(options.Issuer.Fragment))
            {
                failures.Add("TellmaIdentity:Issuer must not contain a query string or fragment.");
            }
            else if (!string.Equals(options.Issuer.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                && !options.Development.AllowInsecureHttp)
            {
                failures.Add("TellmaIdentity:Issuer must use HTTPS (or enable TellmaIdentity:Development:AllowInsecureHttp for local development).");
            }

            // Path base and mode must agree, and the in-proc issuer must live under the path base.
            if (options.Mode == TellmaIdentityDeploymentMode.Standalone)
            {
                if (options.PathBase.Length > 0)
                {
                    failures.Add("TellmaIdentity:PathBase must be empty in Standalone mode.");
                }

                // A standalone issuer is the bare origin: endpoints register unprefixed, so a
                // non-empty issuer path would serve discovery where relying parties never look.
                if (options.Issuer is { IsAbsoluteUri: true } && options.Issuer.AbsolutePath.Trim('/').Length > 0)
                {
                    failures.Add("TellmaIdentity:Issuer must have no path segment in Standalone mode (the issuer is the bare origin).");
                }
            }
            else if (options.Mode == TellmaIdentityDeploymentMode.InProc)
            {
                if (options.PathBase.Length <= 1 || options.PathBase[0] != '/')
                {
                    failures.Add("TellmaIdentity:PathBase is required in InProc mode and must start with '/' (for example \"/id\").");
                }
                else if (options.Issuer is { IsAbsoluteUri: true }
                    && !string.Equals(options.Issuer.AbsolutePath.TrimEnd('/'), options.PathBase.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add("TellmaIdentity:Issuer must end with the configured PathBase in InProc mode.");
                }
            }

            // Exactly one way to reach the store.
            if (options.ConfigureDbContext is null && string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                failures.Add("TellmaIdentity:ConnectionString is required (or supply ConfigureDbContext in code).");
            }

            ValidateCertificate(options, options.Keys.Signing, "Signing", failures);
            ValidateCertificate(options, options.Keys.Encryption, "Encryption", failures);

            // Data Protection: a blob-persisted key ring MUST be encrypted with a Key Vault key,
            // or the DP master keys sit unprotected in blob storage (anyone with blob read access
            // could forge cookies and decrypt WebAuthn state and DP-format tokens).
            if (options.DataProtection.BlobUri is not null && options.DataProtection.KeyVaultKeyUri is null)
            {
                failures.Add("TellmaIdentity:DataProtection:KeyVaultKeyUri is required when BlobUri is set, so the blob-persisted key ring is encrypted at rest.");
            }

            // Email delivery is load-bearing (invitations, recovery): a deployment must either
            // configure SMTP or explicitly opt into the development sink.
            if (string.IsNullOrWhiteSpace(options.Email.SmtpHost) && !options.Development.UseEmailSink)
            {
                failures.Add("TellmaIdentity:Email:SmtpHost is required (or enable TellmaIdentity:Development:UseEmailSink in development).");
            }

            // Lifetimes must be positive; the reuse leeway may be zero (tests) but never negative.
            if (options.Lifetimes.AccessToken <= TimeSpan.Zero
                || options.Lifetimes.IdentityToken <= TimeSpan.Zero
                || options.Lifetimes.RefreshTokenIdle <= TimeSpan.Zero)
            {
                failures.Add("TellmaIdentity:Lifetimes values must be positive.");
            }

            if (options.Lifetimes.RefreshTokenReuseLeeway < TimeSpan.Zero)
            {
                failures.Add("TellmaIdentity:Lifetimes:RefreshTokenReuseLeeway must not be negative.");
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }

        /// <summary>Validates one certificate slot's source configuration.</summary>
        private void ValidateCertificate(
            TellmaIdentityOptions options,
            TellmaIdentityCertificateOptions certificate,
            string slot,
            List<string> failures)
        {
            if (certificate.Source == TellmaIdentityCertificateSourceKind.None)
            {
                failures.Add($"TellmaIdentity:Keys:{slot}:Source must be configured (PfxFile, CertificateStore, KeyVault, or DevelopmentSelfSigned in development).");
            }
            else if (certificate.Source == TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned
                && !options.Development.AllowDevelopmentCertificates)
            {
                failures.Add($"TellmaIdentity:Keys:{slot}: DevelopmentSelfSigned certificates require TellmaIdentity:Development:AllowDevelopmentCertificates and must never be used in production.");
            }
            else if (certificate.Source == TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned
                && environment is not null && !environment.IsDevelopment())
            {
                // Defense-in-depth beyond the opt-in flag: never let a self-signed dev key boot in
                // a non-Development environment, mirroring the dev-admin double guard.
                failures.Add($"TellmaIdentity:Keys:{slot}: DevelopmentSelfSigned certificates are only permitted in the Development environment.");
            }
            else if (certificate.Source == TellmaIdentityCertificateSourceKind.PfxFile
                && !certificate.PfxFiles.Any(static f => !string.IsNullOrWhiteSpace(f.Path)))
            {
                failures.Add($"TellmaIdentity:Keys:{slot}:PfxFiles must contain at least one file path.");
            }
            else if (certificate.Source == TellmaIdentityCertificateSourceKind.CertificateStore
                && !certificate.StoreThumbprints.Any(static t => !string.IsNullOrWhiteSpace(t)))
            {
                failures.Add($"TellmaIdentity:Keys:{slot}:StoreThumbprints must contain at least one thumbprint.");
            }
            else if (certificate.Source == TellmaIdentityCertificateSourceKind.KeyVault
                && (certificate.KeyVault.VaultUri is null || string.IsNullOrWhiteSpace(certificate.KeyVault.CertificateName)))
            {
                failures.Add($"TellmaIdentity:Keys:{slot}:KeyVault requires VaultUri and CertificateName.");
            }
        }
    }
}
