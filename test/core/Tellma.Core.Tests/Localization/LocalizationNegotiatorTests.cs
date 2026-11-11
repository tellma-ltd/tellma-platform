// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     Locale negotiation: language, region, calendar, display zone and content language, each
    ///     across what a surface fixed, the request headers, the stored preferences and the tenant.
    /// </summary>
    /// <remarks>
    ///     The distribution is Acme's unless a test says otherwise: English with British formats
    ///     first, then Arabic with Saudi formats. The tenant's content languages are English and
    ///     Arabic, its calendars Gregorian then Umm al-Qura, and its zone Riyadh's.
    /// </remarks>
    public sealed class LocalizationNegotiatorTests : IDisposable
    {
        private readonly ServiceProvider _metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        private readonly MetricCollector<long> _rejected;
        private readonly LocalizationNegotiator _negotiator;

        public LocalizationNegotiatorTests()
        {
            // The real factory, because the collector matches on the meter's scope.
            _rejected = new MetricCollector<long>(
                _metrics.GetRequiredService<IMeterFactory>(), LocalizationTelemetryNames.MeterName, LocalizationTelemetryNames.HeadersRejected);
            _negotiator = Negotiator(["en-GB", "ar-SA"]);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _rejected.Dispose();
            _metrics.Dispose();
        }

        // --- Language ---

        [Fact]
        public void With_nothing_asked_the_tenants_primary_language_decides()
        {
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(settings: Tenant(languages: ["ar", "en"]))).Culture.Name);
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(settings: Tenant(languages: ["en", "ar"]))).Culture.Name);
        }

        [Fact]
        public void A_tenant_primary_that_is_not_offered_yields_the_distribution_default()
        {
            // French is catalogued but Acme does not offer it; the first declared language stands in.
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(settings: Tenant(languages: ["fr"]))).Culture.Name);
            Assert.Equal("ar-SA", Negotiator(["ar-SA", "en-GB"]).Negotiate(Input(settings: Tenant(languages: ["fr"]))).Culture.Name);
        }

        [Fact]
        public void The_stored_language_comes_before_the_tenants()
        {
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(preferredLanguage: "ar")).Culture.Name);
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(preferredLanguage: "AR")).Culture.Name);

            // A stored language the distribution stopped offering is skipped.
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(preferredLanguage: "fr")).Culture.Name);
        }

        [Fact]
        public void The_header_comes_before_the_stored_language()
        {
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(acceptLanguage: "ar", preferredLanguage: "en")).Culture.Name);
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(acceptLanguage: "en", preferredLanguage: "ar")).Culture.Name);
        }

        [Theory]
        [InlineData("fr-FR,ar-SA;q=0.9,en;q=0.8", "ar-SA")]
        [InlineData("fr,de,ar", "ar-SA")]
        [InlineData("en;q=0.2,ar;q=0.9", "ar-SA")]
        [InlineData("ar-EG-u-nu-arab", "ar-EG")]
        [InlineData("AR", "ar-SA")]
        public void The_first_offered_language_among_the_first_three_ranges_wins(string header, string culture)
        {
            Assert.Equal(culture, _negotiator.Negotiate(Input(acceptLanguage: header)).Culture.Name);
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        [Fact]
        public void Ranges_after_the_third_take_no_part()
        {
            // Arabic is the fourth range, so the header decides nothing and the stored language does.
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(acceptLanguage: "fr,de,es,ar")).Culture.Name);
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(acceptLanguage: "fr,de,es,en", preferredLanguage: "ar")).Culture.Name);
        }

        [Fact]
        public void A_well_formed_header_naming_no_offered_language_is_passed_over_and_not_counted()
        {
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(acceptLanguage: "ja-JP,ko;q=0.5,*;q=0.1", preferredLanguage: "ar")).Culture.Name);

            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        [Theory]
        [InlineData("ar_SA")]
        [InlineData("ar;q=high")]
        [InlineData("???")]
        public void An_unparseable_header_is_ignored_and_counted(string header)
        {
            LocalizationContext context = _negotiator.Negotiate(Input(acceptLanguage: header));

            Assert.Equal("en-GB", context.Culture.Name);
            Assert.Equal("accept-language", Assert.Single(_rejected.GetMeasurementSnapshot()).Tags[LocalizationTelemetryNames.HeaderTag]);
        }

        [Fact]
        public void The_sound_ranges_of_a_partly_malformed_header_still_count()
        {
            LocalizationContext context = _negotiator.Negotiate(Input(acceptLanguage: "e_n, ar-AE;q=0.9"));

            Assert.Equal("ar-AE", context.Culture.Name);
            Assert.Equal(1, Assert.Single(_rejected.GetMeasurementSnapshot()).Value);
        }

        [Fact]
        public void A_language_matches_by_its_subtag_whatever_script_or_region_follows()
        {
            LocalizationNegotiator negotiator = Negotiator(["en", "zh"]);

            Assert.Equal("zh-CN", negotiator.Negotiate(Input(acceptLanguage: "zh-Hans-CN")).Culture.Name);
            Assert.Equal("zh-TW", negotiator.Negotiate(Input(acceptLanguage: "zh-Hant-TW")).Culture.Name);
            Assert.Equal("zh-CN", negotiator.Negotiate(Input(acceptLanguage: "zh-Hant")).Culture.Name);
        }

        // --- Region ---

        [Fact]
        public void The_matched_ranges_region_comes_first()
        {
            Assert.Equal("ar-AE", _negotiator.Negotiate(Input(acceptLanguage: "ar-AE", preferredRegion: "EG", settings: Tenant(region: "KW"))).Culture.Name);
            Assert.Equal("ar-AE", _negotiator.Negotiate(Input(acceptLanguage: "ar-ae")).Culture.Name);
        }

        [Fact]
        public void The_stored_region_then_the_tenants_then_the_languages_default()
        {
            // A reader with a stored region gets it with and without a header.
            Assert.Equal("ar-AE", _negotiator.Negotiate(Input(acceptLanguage: "ar", preferredRegion: "AE")).Culture.Name);
            Assert.Equal("ar-AE", _negotiator.Negotiate(Input(preferredLanguage: "ar", preferredRegion: "AE", settings: Tenant(region: "KW"))).Culture.Name);

            // No stored region: the tenant's.
            Assert.Equal("ar-KW", _negotiator.Negotiate(Input(preferredLanguage: "ar", settings: Tenant(region: "KW"))).Culture.Name);

            // Neither: the distribution's default region for the language.
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(preferredLanguage: "ar")).Culture.Name);
        }

        [Fact]
        public void A_language_without_a_declared_region_falls_to_the_catalogues_default()
        {
            LocalizationNegotiator negotiator = Negotiator(["en", "ar"]);

            Assert.Equal("ar-EG", negotiator.Negotiate(Input(acceptLanguage: "ar")).Culture.Name);
            Assert.Equal("en-GB", negotiator.Negotiate(Input(acceptLanguage: "en")).Culture.Name);
        }

        [Fact]
        public void A_region_not_offered_with_the_language_is_skipped_at_every_step()
        {
            // Saudi Arabia is not an English region: the header's, the stored and the tenant's are
            // each passed over, down to English's default.
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(acceptLanguage: "en-SA")).Culture.Name);
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(acceptLanguage: "en-SA", preferredRegion: "SA", settings: Tenant(region: "SA"))).Culture.Name);
            Assert.Equal("en-AE", _negotiator.Negotiate(Input(acceptLanguage: "en-SA", preferredRegion: "SA", settings: Tenant(region: "AE"))).Culture.Name);
            Assert.Equal("en-US", _negotiator.Negotiate(Input(acceptLanguage: "en-SA", preferredRegion: "us", settings: Tenant(region: "AE"))).Culture.Name);
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(acceptLanguage: "en-ZZ", preferredRegion: "001")).Culture.Name);

            // A skipped region is no invalid header.
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        // --- A surface's fixed message language ---

        [Fact]
        public void A_fixed_message_language_sets_the_culture_and_the_content_language_follows_the_reader()
        {
            // The message is in English; the reader's own language is Arabic, the tenant's second.
            LocalizationContext context = _negotiator.Negotiate(Input(acceptLanguage: "ar-SA", messageLanguage: "en"));

            Assert.Equal("en-GB", context.Culture.Name);
            Assert.Equal(2, context.ContentLanguageIndex);

            // And from a stored language, with no header at all.
            context = _negotiator.Negotiate(Input(preferredLanguage: "ar", messageLanguage: "en"));
            Assert.Equal("en-GB", context.Culture.Name);
            Assert.Equal(2, context.ContentLanguageIndex);
        }

        [Fact]
        public void A_fixed_message_language_that_is_not_offered_is_ignored()
        {
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(acceptLanguage: "ar", messageLanguage: "fr")).Culture.Name);
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(acceptLanguage: "ar", messageLanguage: "not a language")).Culture.Name);
            Assert.Equal("ar-SA", _negotiator.Negotiate(Input(acceptLanguage: "ar", messageLanguage: "  ")).Culture.Name);
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        [Fact]
        public void A_fixed_message_language_takes_the_readers_region_when_it_is_offered_with_it()
        {
            // The Emirates are an English region too, Saudi Arabia is not.
            Assert.Equal("en-AE", _negotiator.Negotiate(Input(acceptLanguage: "ar-AE", messageLanguage: "en")).Culture.Name);
            Assert.Equal("en-GB", _negotiator.Negotiate(Input(acceptLanguage: "ar-SA", messageLanguage: "en")).Culture.Name);

            // A region the surface fixed with its language comes before the reader's.
            Assert.Equal("en-US", _negotiator.Negotiate(Input(acceptLanguage: "ar-AE", messageLanguage: "en-US")).Culture.Name);
        }

        // --- Content language ---

        [Theory]
        [InlineData("en", 1)]
        [InlineData("ar", 2)]
        [InlineData("am", 3)]
        public void The_content_language_is_the_position_of_the_tenant_language_equal_to_the_readers(string language, int index)
        {
            LocalizationNegotiator negotiator = Negotiator(["en", "ar", "am"]);

            Assert.Equal(index, negotiator.Negotiate(Input(acceptLanguage: language, settings: Tenant(languages: ["en", "ar", "am"]))).ContentLanguageIndex);
        }

        [Fact]
        public void A_reader_whose_language_is_none_of_the_tenants_reads_the_primary_content_language()
        {
            Assert.Equal(1, _negotiator.Negotiate(Input(acceptLanguage: "ar", settings: Tenant(languages: ["en"]))).ContentLanguageIndex);
            Assert.Equal(1, _negotiator.Negotiate(Input(acceptLanguage: "en", settings: Tenant(languages: ["fr", "ar"]))).ContentLanguageIndex);
        }

        // --- Calendar ---

        [Fact]
        public void With_nothing_asked_the_tenants_primary_calendar_decides()
        {
            Assert.Equal("gc", _negotiator.Negotiate(Input()).Calendar.Code);
            Assert.Equal("uq", _negotiator.Negotiate(Input(settings: Tenant(calendars: ["uq", "gc"]))).Calendar.Code);
        }

        [Fact]
        public void The_requested_then_the_stored_calendar_when_it_is_one_of_the_tenants()
        {
            Assert.Equal("uq", _negotiator.Negotiate(Input(calendarHeader: "uq")).Calendar.Code);
            Assert.Equal("uq", _negotiator.Negotiate(Input(calendarHeader: "UQ")).Calendar.Code);
            Assert.Equal("uq", _negotiator.Negotiate(Input(preferredCalendar: "uq")).Calendar.Code);
            Assert.Equal("gc", _negotiator.Negotiate(Input(calendarHeader: "gc", preferredCalendar: "uq")).Calendar.Code);
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        [Theory]
        [InlineData("et")]
        [InlineData("persian")]
        [InlineData("u-ca-islamic")]
        public void A_requested_calendar_outside_the_tenants_is_ignored_and_counted(string header)
        {
            // The Ethiopian calendar is composed, but this tenant does not use it.
            LocalizationContext context = _negotiator.Negotiate(Input(calendarHeader: header, preferredCalendar: "uq"));

            Assert.Equal("uq", context.Calendar.Code);
            Assert.Equal("calendar", Assert.Single(_rejected.GetMeasurementSnapshot()).Tags[LocalizationTelemetryNames.HeaderTag]);
        }

        [Fact]
        public void A_stored_calendar_the_tenant_dropped_falls_to_the_tenants_primary()
        {
            Assert.Equal("gc", _negotiator.Negotiate(Input(preferredCalendar: "et")).Calendar.Code);
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        [Fact]
        public void A_fixed_message_calendar_is_taken_even_outside_the_tenants()
        {
            Assert.Equal("et", _negotiator.Negotiate(Input(calendarHeader: "uq", preferredCalendar: "uq", messageCalendar: "et")).Calendar.Code);
            Assert.Equal("gc", _negotiator.Negotiate(Input(calendarHeader: "uq", messageCalendar: "GC")).Calendar.Code);

            // One that is not composed is no override.
            Assert.Equal("uq", _negotiator.Negotiate(Input(calendarHeader: "uq", messageCalendar: "persian")).Calendar.Code);
        }

        [Fact]
        public void The_calendar_is_never_taken_from_the_language_header()
        {
            LocalizationContext context = _negotiator.Negotiate(Input(acceptLanguage: "ar-SA-u-ca-islamic-umalqura"));

            Assert.Equal("ar-SA", context.Culture.Name);
            Assert.Equal("gc", context.Calendar.Code);
        }

        // --- Display zone ---

        [Fact]
        public void With_nothing_asked_the_tenants_zone_decides()
        {
            Assert.Equal("Asia/Riyadh", _negotiator.Negotiate(Input()).TimeZone.Id);
        }

        [Fact]
        public void The_requested_then_the_stored_zone_when_it_is_a_value_of_the_set()
        {
            Assert.Equal("Asia/Kolkata", _negotiator.Negotiate(Input(timeZoneHeader: "Asia/Kolkata", preferredTimeZone: "Europe/London")).TimeZone.Id);
            Assert.Equal("Europe/London", _negotiator.Negotiate(Input(preferredTimeZone: "Europe/London")).TimeZone.Id);

            // Matched without regard to case, and resolved as the set spells it.
            Assert.Equal("Asia/Kolkata", _negotiator.Negotiate(Input(timeZoneHeader: "asia/kolkata")).TimeZone.Id);
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        [Theory]
        [InlineData("Asia/Calcutta")]
        [InlineData("Mars/Phobos")]
        [InlineData("+03:00")]
        [InlineData("Arab Standard Time")]
        public void A_requested_zone_outside_the_set_is_ignored_and_counted(string header)
        {
            // An alias is not a value: the client maps the device's zone through the aliases first.
            LocalizationContext context = _negotiator.Negotiate(Input(timeZoneHeader: header, preferredTimeZone: "Europe/London"));

            Assert.Equal("Europe/London", context.TimeZone.Id);
            Assert.Equal("time-zone", Assert.Single(_rejected.GetMeasurementSnapshot()).Tags[LocalizationTelemetryNames.HeaderTag]);
        }

        [Fact]
        public void A_stored_zone_outside_the_set_falls_to_the_tenants()
        {
            Assert.Equal("Asia/Riyadh", _negotiator.Negotiate(Input(preferredTimeZone: "Asia/Calcutta")).TimeZone.Id);
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        // --- Across the values ---

        [Fact]
        public void Blank_headers_are_absent()
        {
            LocalizationContext context = _negotiator.Negotiate(Input(acceptLanguage: " ", calendarHeader: string.Empty, timeZoneHeader: "\t"));

            Assert.Equal("en-GB", context.Culture.Name);
            Assert.Equal("gc", context.Calendar.Code);
            Assert.Equal("Asia/Riyadh", context.TimeZone.Id);
            Assert.Empty(_rejected.GetMeasurementSnapshot());
        }

        [Fact]
        public void Each_invalid_header_is_counted_once_under_its_own_tag()
        {
            _ = _negotiator.Negotiate(Input(acceptLanguage: "!!", calendarHeader: "zz", timeZoneHeader: "Nowhere/None"));

            Assert.Equal(
                ["accept-language", "calendar", "time-zone"],
                _rejected.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[LocalizationTelemetryNames.HeaderTag]!).Order());
        }

        [Fact]
        public void The_negotiated_culture_is_the_pinned_latin_digit_gregorian_one()
        {
            CultureInfo culture = _negotiator.Negotiate(Input(acceptLanguage: "ar-SA-u-nu-arab")).Culture;

            Assert.Same(NegotiatedCultures.Get("ar", "SA"), culture);
            Assert.Equal("1,234,567.89", 1234567.891m.ToString("N2", culture));
            _ = Assert.IsType<GregorianCalendar>(culture.DateTimeFormat.Calendar);
        }

        [Theory]
        [InlineData("\0\0\0", "￿", "\n")]
        [InlineData(";;;;,,,,;q=", "", "  ")]
        [InlineData("en-GB;q=0.9999999999999", "gc,uq", "Asia/Riyadh,UTC")]
        [InlineData("a-b-c-d-e-f-g-h-i-j-k-l-m-n-o-p-q-r-s-t-u-v-w-x-y-z", "g" + "c", "Asia")]
        public void No_header_value_makes_negotiation_throw(string acceptLanguage, string calendar, string timeZone)
        {
            LocalizationContext context = _negotiator.Negotiate(Input(
                acceptLanguage: acceptLanguage,
                calendarHeader: calendar,
                timeZoneHeader: timeZone,
                preferredLanguage: acceptLanguage,
                preferredCalendar: calendar,
                preferredTimeZone: timeZone,
                preferredRegion: calendar,
                messageLanguage: acceptLanguage,
                messageCalendar: timeZone));

            Assert.NotNull(context.Culture);
            Assert.NotNull(context.Calendar);
            Assert.NotNull(context.TimeZone);
            Assert.InRange(context.ContentLanguageIndex, 1, 3);
        }

        [Fact]
        public void A_tenant_without_languages_or_calendars_still_negotiates()
        {
            // Never a running tenant, but negotiation may not throw on one.
            LocalizationContext context = _negotiator.Negotiate(Input(settings: Tenant(languages: [], calendars: [])));

            Assert.Equal("en-GB", context.Culture.Name);
            Assert.Equal("gc", context.Calendar.Code);
            Assert.Equal(1, context.ContentLanguageIndex);
        }

        [Fact]
        public void Null_arguments_are_refused()
        {
            _ = Assert.Throws<ArgumentNullException>(() => _negotiator.Negotiate(null!));
            _ = Assert.Throws<ArgumentNullException>(() => _negotiator.Negotiate(Input() with { Settings = null! }));
        }

        // --- Composition ---

        [Fact]
        public void A_composed_host_negotiates_over_its_own_declaration_and_counts_on_its_own_meter()
        {
            using ServiceProvider provider = TestComposition.Compose(static tellma => tellma.Languages(["ar-AE", "en"]))
                .BuildServiceProvider(validateScopes: true);
            using MetricCollector<long> rejected = new(
                provider.GetRequiredService<IMeterFactory>(), LocalizationTelemetryNames.MeterName, LocalizationTelemetryNames.HeadersRejected);

            ILocalizationNegotiator negotiator = provider.GetRequiredService<ILocalizationNegotiator>();
            LocalizationContext context = negotiator.Negotiate(Input(calendarHeader: "nope", settings: Tenant(languages: ["ar", "en"])));

            // One of each for the host, over the distribution's declaration: Arabic is its
            // default language and the Emirates its Arabic readers' default region.
            Assert.Same(negotiator, provider.GetRequiredService<ILocalizationNegotiator>());
            _ = Assert.IsType<LocalizationNegotiator>(negotiator);
            _ = Assert.IsType<LanguageCatalog>(provider.GetRequiredService<ILanguageCatalog>());
            _ = Assert.IsType<ValueSetRegistry>(provider.GetRequiredService<IValueSetRegistry>());
            Assert.Equal("ar-AE", context.Culture.Name);
            Assert.Equal(1, Assert.Single(rejected.GetMeasurementSnapshot()).Value);
        }

        /// <summary>The platform's three calendars, as a registry.</summary>
        /// <returns>The registry.</returns>
        private static CalendarRegistry Calendars()
        {
            return new CalendarRegistry([new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem()]);
        }

        /// <summary>A negotiator over a distribution's declared languages.</summary>
        /// <param name="codes">The declared codes.</param>
        /// <returns>The negotiator, counting on this test's meter.</returns>
        private LocalizationNegotiator Negotiator(string[] codes)
        {
            LanguageCatalog languages = new(new LanguageDeclaration(codes, []));
            CalendarRegistry calendars = Calendars();
            return new LocalizationNegotiator(
                languages, calendars, new ValueSetRegistry(languages, calendars, []), _metrics.GetRequiredService<IMeterFactory>());
        }

        /// <summary>A negotiation input; everything absent unless given.</summary>
        /// <param name="acceptLanguage">The language header.</param>
        /// <param name="calendarHeader">The calendar header.</param>
        /// <param name="timeZoneHeader">The zone header.</param>
        /// <param name="preferredLanguage">The stored language.</param>
        /// <param name="preferredCalendar">The stored calendar.</param>
        /// <param name="preferredTimeZone">The stored zone.</param>
        /// <param name="preferredRegion">The stored region.</param>
        /// <param name="messageLanguage">The surface's fixed message language.</param>
        /// <param name="messageCalendar">The surface's fixed message calendar.</param>
        /// <param name="settings">The tenant's settings; the default tenant's when null.</param>
        /// <returns>The input.</returns>
        private static NegotiationInput Input(
            string? acceptLanguage = null,
            string? calendarHeader = null,
            string? timeZoneHeader = null,
            string? preferredLanguage = null,
            string? preferredCalendar = null,
            string? preferredTimeZone = null,
            string? preferredRegion = null,
            string? messageLanguage = null,
            string? messageCalendar = null,
            TenantSettings? settings = null)
        {
            return new NegotiationInput(
                acceptLanguage,
                calendarHeader,
                timeZoneHeader,
                preferredLanguage,
                preferredCalendar,
                preferredTimeZone,
                preferredRegion,
                messageLanguage,
                messageCalendar,
                settings ?? Tenant());
        }

        /// <summary>A tenant's settings.</summary>
        /// <param name="languages">The content languages; English then Arabic when null.</param>
        /// <param name="calendars">The calendars; Gregorian then Umm al-Qura when null.</param>
        /// <param name="region">The tenant's region.</param>
        /// <returns>The settings, in Riyadh's zone.</returns>
        private static TenantSettings Tenant(string[]? languages = null, string[]? calendars = null, string? region = null)
        {
            LanguageCatalog catalog = new(LanguageDeclaration.None);
            CalendarRegistry registry = Calendars();
            languages ??= ["en", "ar"];

            return new TenantSettings
            {
                TenantId = 7,
                Tag = VersionTag.New(),
                Names = ["Acme", null, null],
                Languages = [.. languages.Select(code => catalog.Find(code)!)],
                Shape = (MultilingualShape)Math.Max(1, languages.Length),
                Calendars = [.. (calendars ?? ["gc", "uq"]).Select(code => registry[code])],
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"),
                SqlServerTimeZoneName = "Arab Standard Time",
                Region = region,
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = new Dictionary<string, JsonElement>(),
            };
        }
    }
}
