// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Identity.Services;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Turns the resources a grant names into the places a person recognizes, for the consent
    ///     and device-approval screens: a distribution's origin is shown as its host, and one of its
    ///     tenants as that host followed by the tenant id, the way the tenant's own address reads.
    ///     <para>
    ///         Hosts are shown in their ASCII (punycode) form, so a look-alike host built from
    ///         other scripts cannot pass for a familiar one. A resource of any other shape is listed
    ///         as it was requested rather than hidden: a place the screen cannot explain is exactly
    ///         the one a user most needs to see.
    ///     </para>
    /// </summary>
    public static class ConsentTargetCatalog
    {
        /// <summary>Describes each requested resource.</summary>
        /// <param name="resources">The grant's resources.</param>
        /// <returns>One entry per distinct description, in the order requested.</returns>
        public static IReadOnlyList<string> Describe(IEnumerable<string> resources)
        {
            ArgumentNullException.ThrowIfNull(resources);

            List<string> described = [];
            foreach (string resource in resources)
            {
                string place = DescribeOne(resource);
                if (!described.Contains(place, StringComparer.Ordinal))
                {
                    described.Add(place);
                }
            }

            return described;
        }

        /// <summary>One resource's description: a host, a host and tenant id, or the raw value.</summary>
        private static string DescribeOne(string resource)
        {
            return ResourceGrantEvaluator.TryParseTenantResource(resource, out string origin, out int tenantId)
                ? Host(origin) + "/" + tenantId.ToString(CultureInfo.InvariantCulture)
                : ResourceGrantEvaluator.IsOrigin(resource) ? Host(resource) : resource;
        }

        /// <summary>A canonical origin's host in ASCII form, with its port when it is not the scheme's default.</summary>
        private static string Host(string origin)
        {
            Uri uri = new(origin, UriKind.Absolute);
            return uri.IsDefaultPort
                ? uri.IdnHost
                : uri.IdnHost + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        }
    }
}
