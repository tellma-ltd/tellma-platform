// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Builds the Excel codec's context from the scope's request context: the tenant, the
    ///     deployment, the unit of work's instant, the tenant's content languages in slot order, and
    ///     the reader's culture, calendar and display zone: the request's own, or, for a background
    ///     export or import, the one negotiated again from the request that started it. Scoped.
    /// </summary>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="deployment">The deployment's identity.</param>
    /// <param name="labels">The label provider.</param>
    /// <param name="catalog">The language catalog.</param>
    /// <param name="negotiator">The locale negotiator, which a job's stored localization is negotiated again through.</param>
    internal sealed class ExcelContextFactory(
        IRequestContextAccessor accessor,
        DeploymentIdentity deployment,
        ILabelProvider labels,
        ILanguageCatalog catalog,
        ILocalizationNegotiator negotiator)
    {
        /// <summary>The scope's request context.</summary>
        private readonly IRequestContextAccessor _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));

        /// <summary>The deployment's identity.</summary>
        private readonly DeploymentIdentity _deployment = deployment ?? throw new ArgumentNullException(nameof(deployment));

        /// <summary>The label provider.</summary>
        private readonly ILabelProvider _labels = labels ?? throw new ArgumentNullException(nameof(labels));

        /// <summary>The language catalog.</summary>
        private readonly ILanguageCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        /// <summary>The locale negotiator.</summary>
        private readonly ILocalizationNegotiator _negotiator = negotiator ?? throw new ArgumentNullException(nameof(negotiator));

        /// <summary>The context of the scope's request.</summary>
        /// <returns>The context.</returns>
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant.</exception>
        public ExcelContext Create()
        {
            RequestContext request = _accessor.Current;
            return Create(request, request.CultureInfo, request.CalendarSystem, request.Localization.TimeZone);
        }

        // Spec 0019 §12.2: a job renders exactly as the request that started it would have. Its
        // stored culture, calendar and zone are negotiated again, as the request's headers were, so a
        // value the tenant has since stopped offering falls back as a request's would.
        /// <summary>
        ///     The localization a background export or import renders in, negotiated from the
        ///     localization the request that started it stored against the tenant's current settings.
        /// </summary>
        /// <param name="culture">The stored localization.</param>
        /// <returns>The localization; the caller enters it for the job's labels and messages.</returns>
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant, or its settings are not loaded.</exception>
        public LocalizationContext Localize(ExcelJobCulture culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            RequestContext request = _accessor.Current;
            TenantSettings settings = request.TenantSettings
                ?? throw new InvalidOperationException("The job scope's tenant settings are not loaded, so the job's localization cannot be negotiated.");
            return _negotiator.Negotiate(new NegotiationInput(
                culture.Culture,
                culture.Calendar,
                culture.TimeZone,
                PreferredLanguage: null,
                PreferredCalendar: null,
                PreferredTimeZone: null,
                PreferredRegion: null,
                MessageLanguage: null,
                MessageCalendar: null,
                settings));
        }

        /// <summary>The context of a background export or import, in the localization it renders in.</summary>
        /// <param name="localization">The localization <see cref="Localize" /> negotiated.</param>
        /// <returns>The context, at the job scope's instant.</returns>
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant.</exception>
        public ExcelContext Create(LocalizationContext localization)
        {
            ArgumentNullException.ThrowIfNull(localization);
            return Create(_accessor.Current, localization.Culture, localization.Calendar, localization.TimeZone);
        }

        /// <summary>The context of a scope in a culture, calendar and zone.</summary>
        /// <param name="request">The scope's request context.</param>
        /// <param name="culture">The culture cells and labels render in.</param>
        /// <param name="calendar">The calendar dates render in.</param>
        /// <param name="zone">The display zone.</param>
        /// <returns>The context.</returns>
        private ExcelContext Create(RequestContext request, CultureInfo culture, ICalendarSystem calendar, TimeZoneInfo zone)
        {
            TenantDescriptor tenant = request.RequireTenant();

            // The settings' languages, read by the connect prologue, are the tenant's current ones;
            // the descriptor's stand in before they are read.
            IReadOnlyList<string> languages = request.TenantSettings?.Languages is { Count: > 0 } configured
                ? [.. configured.Select(static language => language.Code)]
                : tenant.Languages;

            return new ExcelContext(tenant.Id, _deployment.DeploymentId, request.Now, languages, culture, calendar, zone, _labels, _catalog);
        }
    }
}
