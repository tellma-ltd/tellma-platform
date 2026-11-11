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

            // Read through the one reader the pages carry the value with, so the language a page
            // renders in and the value its links pass on are never two readings of the query.
            if (CarriedQuery.Read(httpContext.Request).UiLocales is not { } uiLocales)
            {
                return Task.FromResult<ProviderCultureResult?>(null);
            }

            // ui_locales is an ordered preference list; returning every entry lets the localization
            // middleware pick the first one it actually supports (an unsupported leading tag does
            // not force the default).
            List<StringSegment> cultures = [.. uiLocales.Split(' ').Select(static culture => new StringSegment(culture))];

            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(cultures, cultures));
        }
    }
}
