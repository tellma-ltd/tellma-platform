// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     Refuses an identity authority that is not https outside Development: every token the host
    ///     accepts is validated against keys fetched from it.
    /// </summary>
    /// <param name="environment">The host environment.</param>
    internal sealed class IdentityHostOptionsValidator(IHostEnvironment environment) : IValidateOptions<IdentityHostOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, IdentityHostOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.Mode == IdentityMode.Standalone
                && !environment.IsDevelopment()
                && Uri.TryCreate(options.Authority, UriKind.Absolute, out Uri? authority)
                && authority.Scheme != Uri.UriSchemeHttps
                ? ValidateOptionsResult.Fail(
                    $"{IdentityHostOptions.SectionPath}:{nameof(IdentityHostOptions.Authority)} is '{options.Authority}', which is not https; only Development may reach the identity server over http.")
                : ValidateOptionsResult.Success;
        }
    }

    /// <summary>Refuses a link that is neither empty nor an absolute https URL, outside Development.</summary>
    /// <param name="environment">The host environment.</param>
    internal sealed class DistributionLinksOptionsValidator(IHostEnvironment environment) : IValidateOptions<DistributionLinksOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, DistributionLinksOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (environment.IsDevelopment())
            {
                return ValidateOptionsResult.Success;
            }

            List<string> failures = [];
            foreach ((string key, string? value) in new[] { (nameof(options.Terms), options.Terms), (nameof(options.Privacy), options.Privacy) })
            {
                if (!string.IsNullOrEmpty(value) && !DistributionLinksOptions.IsHttpsUrl(value))
                {
                    failures.Add($"{DistributionLinksOptions.SectionPath}:{key} is '{value}'; a link is an absolute https URL or empty.");
                }
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }

    /// <summary>
    ///     Refuses a Data Protection key ring configured by halves: a blob without the Key Vault key
    ///     that wraps it (which would persist the keys unencrypted, or not at all), a key without its
    ///     blob, a location that is not an absolute https URL, and a shared folder named beside the
    ///     blob, of which only one could hold the ring.
    /// </summary>
    internal sealed class DataProtectionHostOptionsValidator : IValidateOptions<DataProtectionHostOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, DataProtectionHostOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            string section = DataProtectionHostOptions.SectionPath;
            bool hasBlob = !string.IsNullOrWhiteSpace(options.BlobUri);
            bool hasKey = !string.IsNullOrWhiteSpace(options.KeyId);
            List<string> failures = [];

            // --- The cloud ring comes in pairs ---
            if (hasBlob != hasKey)
            {
                failures.Add(hasBlob
                    ? $"{section}:{nameof(options.BlobUri)} is set without {nameof(options.KeyId)}; the key ring in Blob storage is always wrapped by a Key Vault key."
                    : $"{section}:{nameof(options.KeyId)} is set without {nameof(options.BlobUri)}; a Key Vault key wraps a key ring kept in Blob storage.");
            }

            // --- Each location an absolute https URL: the ring holds every session's keys ---
            foreach ((string key, string? value) in new[] { (nameof(options.BlobUri), options.BlobUri), (nameof(options.KeyId), options.KeyId) })
            {
                if (!string.IsNullOrWhiteSpace(value) && !IsHttpsUrl(value))
                {
                    failures.Add($"{section}:{key} is '{value}', which is not an absolute https URL.");
                }
            }

            // --- One ring: a shared folder and a blob would each hold keys the other never reads ---
            if (hasBlob && !string.IsNullOrWhiteSpace(options.KeyRingPath))
            {
                failures.Add($"{section}:{nameof(options.KeyRingPath)} and {nameof(options.BlobUri)} are both set; keep the key ring in one of them.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        /// <summary>Whether a value is an absolute https URL.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsHttpsUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
        }
    }

    /// <summary>
    ///     Refuses the framework's own forwarded-headers switch, which trusts the headers of every
    ///     caller: <c>Tellma:ForwardedHeaders</c>, with its known proxies, is the one way in.
    /// </summary>
    /// <param name="configuration">The application's configuration.</param>
    internal sealed class ForwardedHeadersHostOptionsValidator(IConfiguration configuration) : IValidateOptions<ForwardedHeadersHostOptions>
    {
        /// <summary>The configuration key <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> sets.</summary>
        internal const string FrameworkSwitch = "ForwardedHeaders_Enabled";

        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, ForwardedHeadersHostOptions options)
        {
            // Compared as the framework compares it, so exactly what it would enable is refused.
            return string.Equals(configuration[FrameworkSwitch], "true", StringComparison.OrdinalIgnoreCase)
                ? ValidateOptionsResult.Fail(
                    $"ASPNETCORE_FORWARDEDHEADERS_ENABLED ({FrameworkSwitch}) is set, which trusts X-Forwarded-For from every caller; unset it and list the proxies under {ForwardedHeadersHostOptions.SectionPath}.")
                : ValidateOptionsResult.Success;
        }
    }
}
