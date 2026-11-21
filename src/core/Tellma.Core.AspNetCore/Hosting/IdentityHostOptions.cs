// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Identity;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     How the distribution reaches its identity server, bound from <c>Tellma:Identity</c>.
    /// </summary>
    /// <remarks>
    ///     The secrets are never quoted by a validation message, a log or the information document.
    /// </remarks>
    public sealed class IdentityHostOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from; the identity-server client's options bind from it too.</summary>
        public const string SectionPath = IdentityServerClientOptions.SectionPath;

        /// <summary>The path the in-proc identity engine is served under, the one the identity-server client derives its authority from.</summary>
        public const string InProcPath = IdentityServerClientOptions.InProcPath;

        /// <summary>Where the identity server runs; <see cref="IdentityMode.Standalone" /> by default.</summary>
        public IdentityMode Mode { get; set; } = IdentityMode.Standalone;

        /// <summary>
        ///     The standalone identity server's issuer, such as <c>https://identity.tellma.com</c>:
        ///     required in standalone mode, ignored in-proc, where the authority is the host's own
        ///     origin under <see cref="InProcPath" />.
        /// </summary>
        public string? Authority { get; set; }

        /// <summary>The secret of the distribution's BFF client at the identity server.</summary>
        public string? ClientSecret { get; set; }

        /// <summary>The secret of the distribution's service client at the identity server.</summary>
        public string? ServiceClientSecret { get; set; }

        /// <summary>The issuer every token the host accepts names: the configured authority, or the in-proc engine's.</summary>
        /// <param name="publicOrigin">The distribution's public origin.</param>
        /// <returns>The authority, without a trailing slash.</returns>
        public string ResolveAuthority(string publicOrigin)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(publicOrigin);
            return Mode == IdentityMode.InProc
                ? publicOrigin + InProcPath
                : Authority ?? throw new InvalidOperationException($"{SectionPath}:{nameof(Authority)} is not set; standalone identity mode requires it.");
        }

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!Enum.IsDefined(Mode))
            {
                yield return new ValidationResult($"{SectionPath}:{nameof(Mode)} is '{Mode}'; it is Standalone or InProc.", [nameof(Mode)]);
            }

            // The in-proc authority derives from the public origin, which its own check validates.
            if (Mode == IdentityMode.Standalone && Describe(Authority) is string problem)
            {
                yield return new ValidationResult(problem, [nameof(Authority)]);
            }
        }

        /// <summary>What is wrong with a standalone authority, if anything.</summary>
        /// <param name="authority">The configured value.</param>
        /// <returns>The problem, or null when the value is sound.</returns>
        private static string? Describe(string? authority)
        {
            const string Key = SectionPath + ":" + nameof(Authority);
            if (string.IsNullOrWhiteSpace(authority))
            {
                return $"{Key} is not set; standalone identity mode requires the identity server's issuer.";
            }

            // The scheme separator is required explicitly: on Linux a rooted path parses as a file URI.
            if (!authority.Contains("://", StringComparison.Ordinal) || !Uri.TryCreate(authority, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return $"{Key} is '{authority}', which is not an absolute http or https URI.";
            }

            // Derived URIs such as the account link append a path, so a trailing slash would double it.
            return authority.EndsWith('/') || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0
                ? $"{Key} is '{authority}', which carries a trailing slash, a query, a fragment or user information."
                : null;
        }
    }
}
