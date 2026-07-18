// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Validates every <c>returnUrl</c> the identity pages accept: local URLs only, so no
    ///     page can be turned into an open redirector. Absolute post-logout destinations go
    ///     exclusively through OpenIddict's registered redirect validation, never through here.
    /// </summary>
    public static class ReturnUrlValidator
    {
        /// <summary>Whether a return URL is a safe local target.</summary>
        /// <param name="returnUrl">The candidate value.</param>
        /// <returns>True for local app-relative URLs.</returns>
        public static bool IsValid(string? returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl))
            {
                return false;
            }

            // Local means "/..." but not "//..." (protocol-relative) and not "/\..." (browser
            // backslash normalization); a second character is required after '/'.
            return returnUrl[0] == '/'
                && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'));
        }

        /// <summary>Returns the URL when valid, else the given local fallback.</summary>
        /// <param name="returnUrl">The candidate value.</param>
        /// <param name="fallback">The local fallback destination.</param>
        /// <returns>A safe local URL.</returns>
        public static string Sanitize(string? returnUrl, string fallback)
        {
            ArgumentException.ThrowIfNullOrEmpty(fallback);

            return IsValid(returnUrl) ? returnUrl! : fallback;
        }
    }
}
