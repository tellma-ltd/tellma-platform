// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The About dialog's links, bound from <c>Tellma:Links</c>: each an absolute https URL or
    ///     empty, which startup enforces outside Development.
    /// </summary>
    public sealed class DistributionLinksOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Links";

        /// <summary>The terms of service.</summary>
        public string? Terms { get; set; }

        /// <summary>The privacy policy.</summary>
        public string? Privacy { get; set; }

        /// <summary>Whether a link is an absolute https URL.</summary>
        /// <param name="value">The configured value.</param>
        /// <returns>True when it is.</returns>
        internal static bool IsHttpsUrl(string value)
        {
            return value.Contains("://", StringComparison.Ordinal)
                && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                && uri.Scheme == Uri.UriSchemeHttps;
        }
    }
}
