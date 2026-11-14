// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Identity;

namespace Tellma.Core.Identity
{
    // Spec 0018 §2.1: Authority absolute, https except in Development; ServiceClientSecret non-empty.
    /// <summary>
    ///     Validates <see cref="IdentityServerClientOptions" /> after the platform settled them, through
    ///     the startup gate's options check: the authority is an absolute <c>https</c> URI (<c>http</c>
    ///     only in Development) with no trailing slash, query, fragment or user information, and the
    ///     service client's secret is set, except in in-proc Development, where only the web host holds
    ///     the one it generates for its boot. No message quotes the secret.
    /// </summary>
    /// <param name="environment">The host environment, read when the options are validated.</param>
    /// <param name="configuration">The host's configuration, for the identity mode.</param>
    internal sealed class IdentityServerClientOptionsValidator(IHostEnvironment environment, IConfiguration configuration)
        : IValidateOptions<IdentityServerClientOptions>
    {
        /// <summary>The prefix of every key a message names.</summary>
        private const string Prefix = IdentityServerClientOptions.SectionPath + ":";

        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, IdentityServerClientOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            bool development = environment.IsDevelopment();
            bool inProc = IdentityComposition.IsInProc(configuration);
            List<string> failures = [];

            // --- The authority ---
            if (DescribeAuthority(options.Authority, development, inProc) is string authority)
            {
                failures.Add(authority);
            }

            // --- The service client ---
            if (string.IsNullOrWhiteSpace(options.ClientId))
            {
                failures.Add("The identity server's service client id is not set; the platform derives it from the distribution's slug.");
            }

            if (string.IsNullOrWhiteSpace(options.ServiceClientSecret) && !(inProc && development))
            {
                failures.Add(
                    $"{Prefix}{nameof(IdentityServerClientOptions.ServiceClientSecret)} is not set; the distribution's service client authenticates with it to invite users and manage service accounts at the identity server"
                    + (inProc ? ", and outside Development the in-proc identity engine seeds the client with it, so every instance must share one stable secret." : "."));
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        /// <summary>What is wrong with the authority, if anything.</summary>
        /// <param name="authority">The settled authority.</param>
        /// <param name="development">Whether the host runs in Development, where http is allowed.</param>
        /// <param name="inProc">Whether the authority was derived from the public origin.</param>
        /// <returns>The problem, or null for a sound authority.</returns>
        internal static string? DescribeAuthority(Uri? authority, bool development, bool inProc)
        {
            const string Key = Prefix + nameof(IdentityServerClientOptions.Authority);
            if (authority is null)
            {
                return inProc
                    ? $"{Key} cannot be derived: in in-proc identity mode it is the in-proc engine's issuer, {{Tellma:PublicOrigin}}{IdentityServerClientOptions.InProcPath}, and Tellma:PublicOrigin is not a valid origin."
                    : $"{Key} is not set; the identity server's issuer URL is required.";
            }

            // A value with user information is never quoted: it may carry a password.
            string quoted = authority.IsAbsoluteUri && authority.UserInfo.Length > 0 ? "a URI with user information" : $"'{authority.OriginalString}'";

            // On Linux a rooted path such as '/id' binds as an absolute file URI, so the scheme decides.
            if (!authority.IsAbsoluteUri || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
            {
                return $"{Key} is {quoted}, which is not an absolute http or https URI.";
            }

            if (authority.Scheme == Uri.UriSchemeHttp && !development)
            {
                return $"{Key} is {quoted}, which is not https; only Development may reach the identity server over http.";
            }

            // The management routes are appended to the authority's path, so a trailing slash would
            // double the separator; a query, a fragment or user information belongs to no issuer.
            return authority.OriginalString.EndsWith('/') || authority.Query.Length > 0 || authority.Fragment.Length > 0 || authority.UserInfo.Length > 0
                ? $"{Key} is {quoted}, which carries a trailing slash, a query, a fragment or user information."
                : null;
        }
    }
}
