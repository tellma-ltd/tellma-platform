// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Localization
{
    // The initializer is spec 0013 §9.3's, at Order 200: after the user connection (spec 0014, 100),
    // it ensures the tenant's settings and negotiates the locale from the request, the user's stored
    // preferences and the tenant's settings (§9.2).
    /// <summary>
    ///     Completes a tenant scope's context with the tenant's settings and the negotiated locale:
    ///     the language and culture, the calendar, the display and tenant zones, today's date in the
    ///     tenant's zone, and the content-language position.
    /// </summary>
    /// <remarks>
    ///     It sets no ambient culture: one set inside an initializer does not flow back to the code
    ///     that runs the work, which applies the completed context's cultures itself
    ///     (<see cref="RequestContext.ApplyCultures" />). A background scope runs it with empty
    ///     inputs, so its locale comes from the run-as user's preferences, then the tenant's.
    /// </remarks>
    /// <param name="settings">The scope's tenant settings.</param>
    /// <param name="negotiator">The locale negotiator.</param>
    /// <param name="time">The clock today's date is read from.</param>
    /// <param name="preferences">The connected user's stored preferences; null in a composition without a user connection.</param>
    internal sealed class LocaleNegotiationInitializer(
        ITenantSettingsCache settings,
        ILocalizationNegotiator negotiator,
        TimeProvider time,
        IUserLocalePreferences? preferences = null) : IRequestContextInitializer
    {
        /// <summary>The initializer's order: after the user connection at 100.</summary>
        public const int NegotiationOrder = 200;

        /// <inheritdoc />
        public int Order => NegotiationOrder;

        /// <inheritdoc />
        public async Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(inputs);

            // Tenantless work has no settings to negotiate against: it keeps the platform's defaults.
            if (context.Tenant is null)
            {
                return context;
            }

            // --- The tenant's settings: cached, else loaded once ---
            CacheResult<TenantSettings> loaded = await settings.GetAsync().ConfigureAwait(false);
            TenantSettings tenant = loaded.Value
                ?? throw new InvalidOperationException("The tenant settings cache served no settings.");

            // --- The connected user's stored preferences, when the scope is connected as one ---
            UserLocalePreferences? stored = context.UserId is int userId && preferences is not null
                ? await preferences.GetAsync(userId, cancellationToken).ConfigureAwait(false)
                : null;

            // --- The negotiation: override, header, preference, tenant, distribution default ---
            LocalizationContext locale = negotiator.Negotiate(new NegotiationInput(
                inputs.AcceptLanguage,
                inputs.RequestedCalendar,
                inputs.RequestedTimeZone,
                stored?.Language,
                stored?.Calendar,
                stored?.TimeZone,
                stored?.Region,
                inputs.MessageLanguage,
                inputs.MessageCalendar,
                tenant));

            return context with
            {
                Language = LanguageOf(locale.Culture.Name),
                Culture = locale.Culture.Name,
                CultureInfo = locale.Culture,
                CalendarSystem = locale.Calendar,
                TimeZone = locale.TimeZone.Id,
                TenantTimeZone = tenant.TimeZone.Id,
                Today = tenant.Today(time),
                ContentLanguageIndex = locale.ContentLanguageIndex,
                TenantSettings = tenant,
            };
        }

        /// <summary>The language of a negotiated culture's name, <c>&lt;language&gt;-&lt;region&gt;</c>.</summary>
        /// <param name="culture">The culture's name.</param>
        /// <returns>The language: the name without its last subtag, or the whole name when it has one subtag.</returns>
        private static string LanguageOf(string culture)
        {
            int dash = culture.LastIndexOf('-');
            return dash > 0 ? culture[..dash] : culture;
        }
    }
}
