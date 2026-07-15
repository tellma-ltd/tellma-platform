// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

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

            // ui_locales is a space-delimited preference list; the first entry wins and the
            // localization middleware falls back through its configured cultures.
            string first = uiLocales.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(first));
        }
    }
}
