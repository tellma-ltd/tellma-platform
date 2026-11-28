// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Identity.Services
{
    /// <summary>
    ///     The platform's <see cref="IResourceGrantEvaluator" />: exact matches, plus one fixed
    ///     per-tenant path under a granted origin. Stateless and allocation-light, so it is a
    ///     singleton.
    /// </summary>
    public sealed class ResourceGrantEvaluator : IResourceGrantEvaluator
    {
        /// <summary>The path segment that ends every per-tenant resource.</summary>
        public const string TenantResourceSuffix = "/mcp";

        /// <summary>
        ///     The most digits a tenant id may have: tenant ids are positive 32-bit integers, and
        ///     <see cref="int.MaxValue" /> has ten.
        /// </summary>
        private const int MaxTenantIdDigits = 10;

        /// <inheritdoc />
        public bool IsGranted(IReadOnlySet<string> grantedResources, string requestedResource)
        {
            ArgumentNullException.ThrowIfNull(grantedResources);
            ArgumentNullException.ThrowIfNull(requestedResource);

            // An exact grant first; otherwise the one per-tenant shape, anchored only on a granted
            // value that is itself an origin.
            return IsGrantedExactly(grantedResources, requestedResource)
                || (TryParseTenantResource(requestedResource, out string? origin, out _)
                    && ContainsOrdinal(grantedResources, origin));
        }

        /// <inheritdoc />
        public bool IsGrantedExactly(IReadOnlySet<string> grantedResources, string requestedResource)
        {
            ArgumentNullException.ThrowIfNull(grantedResources);
            ArgumentNullException.ThrowIfNull(requestedResource);

            return requestedResource.Length > 0 && ContainsOrdinal(grantedResources, requestedResource);
        }

        /// <summary>
        ///     Whether a value is an origin in its canonical spelling: an absolute <c>http</c> or
        ///     <c>https</c> URI with a host, no user information, and nothing after the authority —
        ///     not even a trailing slash — spelled exactly as <see cref="Uri.GetLeftPart" /> spells
        ///     it (lowercase scheme and host, no default port). Every origin the platform records is
        ///     written that way, so a value spelled any other way is not one of them.
        /// </summary>
        /// <param name="value">The candidate.</param>
        /// <returns>True when the value is a canonical origin.</returns>
        public static bool IsOrigin(string? value)
        {
            return !string.IsNullOrEmpty(value)
                && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                    || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
                && !string.IsNullOrEmpty(uri.Host)
                && string.IsNullOrEmpty(uri.UserInfo)
                && string.Equals(uri.GetLeftPart(UriPartial.Authority), value, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Splits a per-tenant resource, <c>&lt;origin&gt;/&lt;id&gt;/mcp</c>, into its origin and
        ///     tenant id. The match is textual: the origin must be canonical (see
        ///     <see cref="IsOrigin" />), the id a positive 32-bit decimal without leading zeros or
        ///     sign, and nothing may follow <c>/mcp</c>.
        /// </summary>
        /// <param name="resource">The requested resource.</param>
        /// <param name="origin">The origin the resource sits under, when it is one.</param>
        /// <param name="tenantId">The tenant id, when it is one.</param>
        /// <returns>True when the resource has the per-tenant shape.</returns>
        public static bool TryParseTenantResource(string? resource, out string origin, out int tenantId)
        {
            origin = string.Empty;
            tenantId = 0;

            // The fixed suffix, compared ordinally: "/MCP", "/mcp/" or "/mcp?x" never match.
            if (string.IsNullOrEmpty(resource) || !resource.EndsWith(TenantResourceSuffix, StringComparison.Ordinal))
            {
                return false;
            }

            // What precedes the suffix is "<origin>/<id>"; the id is the last segment.
            string body = resource[..^TenantResourceSuffix.Length];
            int separator = body.LastIndexOf('/');
            if (separator <= 0)
            {
                return false;
            }

            string candidateOrigin = body[..separator];
            ReadOnlySpan<char> id = body.AsSpan(separator + 1);

            // A positive decimal: ASCII digits only, no leading zero (which also refuses 0 itself),
            // and within the 32-bit range tenant ids live in.
            if (id.Length is 0 or > MaxTenantIdDigits || id[0] is < '1' or > '9')
            {
                return false;
            }

            foreach (char digit in id)
            {
                if (digit is < '0' or > '9')
                {
                    return false;
                }
            }

            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                || !IsOrigin(candidateOrigin))
            {
                return false;
            }

            origin = candidateOrigin;
            tenantId = parsed;
            return true;
        }

        /// <summary>
        ///     Membership by ordinal comparison, independent of the set's own comparer, so that a set
        ///     built with a case-insensitive comparer cannot match a differently spelled resource.
        /// </summary>
        private static bool ContainsOrdinal(IReadOnlySet<string> set, string value)
        {
            foreach (string candidate in set)
            {
                if (string.Equals(candidate, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
