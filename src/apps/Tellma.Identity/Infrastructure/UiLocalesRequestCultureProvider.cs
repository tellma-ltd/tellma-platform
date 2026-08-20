// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Primitives;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Honors the OIDC <c>ui_locales</c> hint when a distribution deep-links a user to the
    ///     identity UI, ahead of the culture cookie and <c>Accept-Language</c> defaults.
    /// </summary>
    public sealed class UiLocalesRequestCultureProvider : RequestCultureProvider
    {
        /// <inheritdoc />
        public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            string? uiLocales = httpContext.Request.Query["ui_locales"];
            if (string.IsNullOrWhiteSpace(uiLocales))
            {
                return Task.FromResult<ProviderCultureResult?>(null);
            }

            // ui_locales is an ordered preference list; returning every entry lets the localization
            // middleware pick the first one it actually supports (an unsupported leading tag does
            // not force the default).
            List<StringSegment> cultures =
                [.. uiLocales.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(static culture => new StringSegment(culture))];

            return Task.FromResult(
                cultures.Count == 0 ? null : new ProviderCultureResult(cultures, cultures));
        }
    }
}
