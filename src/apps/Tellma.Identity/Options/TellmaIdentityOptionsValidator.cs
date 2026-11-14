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
    ///     the DI-resolved validator that does have it. The seeded clients are validated only when
    ///     it is set: their rules depend on it, and they shape no registration-time decision.
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

            // Email delivery is load-bearing (invitations, recovery) and still validated at
            // startup — by the email pipeline rather than here, against the Email section and the
            // transport that section selects. Repeating the rule would mean two validators
            // disagreeing about what a usable configuration is.

            // A language the engine ships no resources for would render as English while claiming
            // to be something else, so a typo fails startup instead of shipping a broken picker.
            foreach (string language in options.Ui.Languages)
            {
                if (!Infrastructure.LanguageCatalog.Shipped.Any(
                    shipped => string.Equals(shipped.Culture, language, StringComparison.OrdinalIgnoreCase)))
                {
                    string shipped = string.Join(", ", Infrastructure.LanguageCatalog.Shipped.Select(static l => l.Culture));
                    failures.Add($"TellmaIdentity:Ui:Languages contains '{language}', which is not one of: {shipped}.");
                }
            }

            // Legal links are rendered as-is, so anything that is not an absolute URL is a broken
            // link on every page rather than a missing one.
            foreach ((string setting, string? url) in ((string, string?)[])
                [("PrivacyPolicyUrl", options.Ui.PrivacyPolicyUrl), ("TermsOfServiceUrl", options.Ui.TermsOfServiceUrl)])
            {
                if (!string.IsNullOrWhiteSpace(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    failures.Add($"TellmaIdentity:Ui:{setting} must be an absolute URL.");
                }
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

            // Seed entries shape no registration-time decision, and their rules depend on the
            // environment (Development relaxes the origin scheme and the secrets), so they are
            // validated only once it is known: by the start-up check, over the host's final
            // configuration, never by the eager registration-time one.
            if (environment is not null)
            {
                ValidateSeedClients(options.Seed, environment, failures);
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }

        /// <summary>
        ///     Validates the enabled seed entries — the client list and the interim clients alike:
        ///     every client id they register is unique, each distribution entry is well formed, the
        ///     third-party flag sits on native entries only, and each interim client is a
        ///     third-party native entry whose client id is its key. Every failure names its entry.
        /// </summary>
        private static void ValidateSeedClients(
            TellmaIdentitySeedOptions seed, IHostEnvironment environment, List<string> failures)
        {
            // A distribution entry registers two clients, so its backend id counts too: two entries
            // that resolve to one client id would overwrite each other at every start. Compared
            // ignoring case, as the store's unique index on the client id is under the platform's
            // case-insensitive collation: "ACME-svc" and "acme-svc" are one row there.
            Dictionary<string, string> claimed = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string path, TellmaIdentitySeedClientOptions client, string? interimKey) in seed.ListEntries())
            {
                // A disabled entry seeds nothing, so nothing about it can go wrong at a start.
                if (!client.Enabled)
                {
                    continue;
                }

                string entry = $"{path} ('{client.ClientId}')";

                if (string.IsNullOrWhiteSpace(client.ClientId))
                {
                    failures.Add($"{path}: ClientId is required.");
                    continue;
                }

                IEnumerable<string> clientIds = client.Kind == TellmaIdentitySeedClientKind.Distribution
                    ? [client.ClientId, client.ClientId + Services.Provisioning.DistributionClientRegistration.ServiceClientIdSuffix]
                    : [client.ClientId];
                foreach (string clientId in clientIds)
                {
                    if (!claimed.TryAdd(clientId, entry))
                    {
                        failures.Add($"{entry}: registers client '{clientId}', which {claimed[clientId]} already registers.");
                    }
                }

                if (client.Kind == TellmaIdentitySeedClientKind.Distribution)
                {
                    ValidateDistribution(client, entry, environment.IsDevelopment(), failures);
                }

                // Third-party software is a public native client by definition; on any other kind
                // the flag would either be meaningless or strip a first-party marker the platform's
                // own clients rely on.
                if (client.ThirdParty && client.Kind != TellmaIdentitySeedClientKind.Native)
                {
                    failures.Add($"{entry}: ThirdParty is accepted on Native entries only.");
                }

                // The interim section exists for third-party agents alone: an entry there that is
                // not one would be a first-party client seeded under the agents' name.
                if (interimKey is not null)
                {
                    if (!string.Equals(client.ClientId, interimKey, StringComparison.Ordinal))
                    {
                        failures.Add($"{entry}: an interim client's ClientId must equal its key '{interimKey}'.");
                    }

                    if (client.Kind != TellmaIdentitySeedClientKind.Native || !client.ThirdParty)
                    {
                        failures.Add($"{entry}: an interim client must be a Native entry with ThirdParty set.");
                    }
                }
            }
        }

        /// <summary>
        ///     Validates one distribution entry: its slug, its origin and back-channel logout URI,
        ///     and — outside Development — both secrets.
        /// </summary>
        private static void ValidateDistribution(
            TellmaIdentitySeedClientOptions client, string entry, bool development, List<string> failures)
        {
            // The slug becomes a client id, a host name and Azure resource names, so it keeps to
            // the slug grammar: lowercase letters, digits and hyphens, starting with a letter.
            string slug = client.ClientId!;
            if (slug.Length > TellmaIdentitySeedClientOptions.MaxDistributionSlugLength
                || slug[0] is not (>= 'a' and <= 'z')
                || !slug.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                failures.Add(
                    $"{entry}: a Distribution ClientId is the distribution's slug: lowercase letters, digits and hyphens, "
                        + $"starting with a letter, at most {TellmaIdentitySeedClientOptions.MaxDistributionSlugLength} characters.");
            }

            if (string.IsNullOrWhiteSpace(client.Origin))
            {
                failures.Add($"{entry}: Origin is required for a Distribution entry.");
            }
            else if (!Uri.TryCreate(client.Origin, UriKind.Absolute, out Uri? origin)
                || !IsAcceptedScheme(origin, development)
                || !string.IsNullOrEmpty(origin.UserInfo)
                || origin.AbsolutePath != "/"
                || !string.IsNullOrEmpty(origin.Query)
                || !string.IsNullOrEmpty(origin.Fragment))
            {
                failures.Add(
                    $"{entry}: Origin must be an absolute https origin with no path, query or fragment "
                        + "(http only in the Development environment).");
            }

            if (!string.IsNullOrWhiteSpace(client.BackchannelLogoutUri)
                && (!Uri.TryCreate(client.BackchannelLogoutUri, UriKind.Absolute, out Uri? backchannel)
                    || !IsAcceptedScheme(backchannel, development)
                    || !string.IsNullOrEmpty(backchannel.UserInfo)))
            {
                failures.Add(
                    $"{entry}: BackchannelLogoutUri must be an absolute https URI (http only in the Development environment).");
            }

            // Development lets a host supply per-boot secrets of its own; the seeder never makes one.
            if (!development)
            {
                if (string.IsNullOrWhiteSpace(client.ClientSecret))
                {
                    failures.Add($"{entry}: ClientSecret is required outside the Development environment.");
                }

                if (string.IsNullOrWhiteSpace(client.ServiceClientSecret))
                {
                    failures.Add($"{entry}: ServiceClientSecret is required outside the Development environment.");
                }
            }
        }

        /// <summary>Whether a distribution URI's scheme is accepted: <c>https</c> always, <c>http</c> only in Development.</summary>
        private static bool IsAcceptedScheme(Uri uri, bool development)
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (development && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal));
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
