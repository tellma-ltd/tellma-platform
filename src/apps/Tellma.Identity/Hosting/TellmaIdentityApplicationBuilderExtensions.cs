// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Hosting
{
    /// <summary>Middleware registration for the identity engine.</summary>
    public static class TellmaIdentityApplicationBuilderExtensions
    {

        /// <summary>
        ///     Adds the engine's middleware: request localization (honoring <c>ui_locales</c>),
        ///     security headers on identity responses, and — standalone only — status-code
        ///     re-execution to the error page (an in-proc host owns its own error handling).
        ///     Call before <c>UseAuthentication</c>/<c>UseAuthorization</c>, which the host owns.
        /// </summary>
        /// <param name="app">The application pipeline.</param>
        /// <returns>The application pipeline, for chaining.</returns>
        public static IApplicationBuilder UseTellmaIdentity(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            TellmaIdentityOptions options =
                app.ApplicationServices.GetRequiredService<IOptions<TellmaIdentityOptions>>().Value;

            if (options.Mode == TellmaIdentityDeploymentMode.Standalone)
            {
                // Renders OpenIddict error pass-through (and any bare status code) through the
                // engine's error page.
                app.UseStatusCodePagesWithReExecute("/error");
            }

            // The offered set, not everything shipped: narrowing the languages a deployment
            // presents must also narrow what a query string or a stale cookie can select.
            LanguageCatalog languages = app.ApplicationServices.GetRequiredService<LanguageCatalog>();
            string[] cultures = [.. languages.OfferedCultures];
            RequestLocalizationOptions localization = new RequestLocalizationOptions()
                .SetDefaultCulture(cultures[0])
                .AddSupportedCultures(cultures)
                .AddSupportedUICultures(cultures);

            // ui_locales (a deep-linking distribution's hint) wins over the culture cookie and
            // Accept-Language defaults.
            localization.RequestCultureProviders.Insert(0, new UiLocalesRequestCultureProvider());
            app.UseRequestLocalization(localization);

            // Ahead of the asset endpoints: a fingerprinted asset URL is safe to cache forever,
            // and not doing so re-fetches the brand fonts on every reload.
            app.UseMiddleware<FingerprintedAssetCacheMiddleware>();

            return app.UseMiddleware<SecurityHeadersMiddleware>();
        }
    }
}
