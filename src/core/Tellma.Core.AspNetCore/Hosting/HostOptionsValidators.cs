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
