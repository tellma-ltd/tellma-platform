// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Calendars;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Decides one reader's culture, calendar, display zone and content language from what a
    ///     surface fixed, what the request asked for, what the reader stored and what their tenant
    ///     set, in that order. The platform registers it as a singleton.
    /// </summary>
    /// <remarks>
    ///     <list type="bullet">
    ///         <item>
    ///             <b>Language:</b> the surface's message language when offered; else the first
    ///             offered language among the first three ranges of <c>Accept-Language</c>, by
    ///             language subtag; else the stored language; else the tenant's primary language;
    ///             else the distribution default. A language the distribution does not offer is
    ///             skipped at every step.
    ///         </item>
    ///         <item>
    ///             <b>Region:</b> the region the message language carries; else the matched range's;
    ///             else the stored region; else the tenant's; else the language's default region. A
    ///             region not offered with the negotiated language is skipped at every step, so
    ///             <c>en-SA</c> yields <c>en-GB</c>.
    ///         </item>
    ///         <item>
    ///             <b>Calendar:</b> the surface's message calendar when composed, even outside the
    ///             tenant's calendars; else the requested calendar, then the stored one, each only
    ///             when it is one of the tenant's; else the tenant's primary calendar.
    ///         </item>
    ///         <item>
    ///             <b>Display zone:</b> the requested zone, then the stored one, each only when it is
    ///             a value of the time-zone set that this host resolves; else the tenant's zone.
    ///         </item>
    ///         <item>
    ///             <b>Content language:</b> the position of the tenant content language equal to the
    ///             language negotiated without the surface's message language, else 1: a message in
    ///             a fixed language still names records in the reader's own.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         A request header that is present but invalid (an unparseable language range, a
    ///         calendar that is not one of the tenant's, a zone outside the set) is ignored and
    ///         counted on <see cref="LocalizationTelemetryNames.HeadersRejected" />; it never fails
    ///         the request. A well-formed <c>Accept-Language</c> naming no offered language is not
    ///         invalid and is not counted.
    ///     </para>
    /// </remarks>
    public sealed class LocalizationNegotiator : ILocalizationNegotiator
    {
        /// <summary>The header tag value of an invalid <c>Accept-Language</c>.</summary>
        internal const string AcceptLanguageHeader = "accept-language";

        /// <summary>The header tag value of an invalid calendar header.</summary>
        internal const string CalendarHeader = "calendar";

        /// <summary>The header tag value of an invalid time-zone header.</summary>
        internal const string TimeZoneHeader = "time-zone";

        /// <summary>The calendar of a tenant without calendars and a registry without the Gregorian one: neither reaches a running host.</summary>
        private static readonly GregorianCalendarSystem LastResortCalendar = new();

        private readonly ILanguageCatalog _languages;
        private readonly ICalendarRegistry _calendars;
        private readonly Counter<long> _headersRejected;

        /// <summary>The time-zone set's ids as it spells them, by id without regard to case.</summary>
        private readonly FrozenDictionary<string, string> _zones;

        /// <summary>Creates the negotiator.</summary>
        /// <param name="languages">The language catalogue.</param>
        /// <param name="calendars">The calendar registry.</param>
        /// <param name="valueSets">The value sets, of which the time-zone set decides which zones a reader may ask for.</param>
        /// <param name="meterFactory">Creates the meter the rejected-header counter is on.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public LocalizationNegotiator(
            ILanguageCatalog languages,
            ICalendarRegistry calendars,
            IValueSetRegistry valueSets,
            IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(languages);
            ArgumentNullException.ThrowIfNull(calendars);
            ArgumentNullException.ThrowIfNull(valueSets);
            ArgumentNullException.ThrowIfNull(meterFactory);

            _languages = languages;
            _calendars = calendars;

            // A set with two spellings of one id is refused at startup; the first stands here.
            Dictionary<string, string> zones = new(StringComparer.OrdinalIgnoreCase);
            foreach (ValueSetEntry entry in valueSets.Find(CoreValueSets.TimeZone)?.Values ?? [])
            {
                _ = zones.TryAdd(entry.Value, entry.Value);
            }

            _zones = zones.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            _headersRejected = meterFactory.Create(LocalizationTelemetryNames.MeterName).CreateCounter<long>(
                LocalizationTelemetryNames.HeadersRejected,
                "{header}",
                "Locale request headers that were present but invalid, and were ignored.");
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="input" /> or its settings are null.</exception>
        public LocalizationContext Negotiate(NegotiationInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            TenantSettings settings = input.Settings ?? throw new ArgumentNullException(nameof(input), "The negotiation input carries no tenant settings.");

            // --- Language: the reader's own first, then the surface's override on top of it ---
            LanguageRange own = OwnLanguage(input, settings);
            LanguageRange message = MessageLanguage(input.MessageLanguage);
            string language = message.Language.Length > 0 ? message.Language : own.Language;

            // --- Region: the first of the row that is offered with the negotiated language ---
            LanguageFormats formats = _languages.Formats(language);
            string region = Region(formats, message.Region)
                ?? Region(formats, own.Region)
                ?? Region(formats, input.PreferredRegion)
                ?? Region(formats, settings.Region)
                ?? formats.DefaultRegion;

            CultureInfo culture = NegotiatedCultures.Get(language, region);

            // --- Content language: from the reader's own language, never from the override ---
            int contentLanguageIndex = 1;
            for (int i = 0; i < settings.Languages.Count && i < 3; i++)
            {
                if (string.Equals(settings.Languages[i]?.Code, own.Language, StringComparison.OrdinalIgnoreCase))
                {
                    contentLanguageIndex = i + 1;
                    break;
                }
            }

            return new LocalizationContext(culture, Calendar(input, settings), TimeZone(input, settings), contentLanguageIndex);
        }

        /// <summary>The language the reader gets without a surface's override, and the region their header named with it.</summary>
        /// <param name="input">The negotiation input.</param>
        /// <param name="settings">The tenant's settings.</param>
        /// <returns>The language as the catalogue spells it, with the matched range's region when the header decided.</returns>
        private LanguageRange OwnLanguage(NegotiationInput input, TenantSettings settings)
        {
            // --- 1. Accept-Language: the first offered language among its first three ranges ---
            if (!string.IsNullOrWhiteSpace(input.AcceptLanguage))
            {
                IReadOnlyList<LanguageRange> ranges = AcceptLanguage.Parse(input.AcceptLanguage, out bool malformed);
                if (malformed)
                {
                    Reject(AcceptLanguageHeader);
                }

                foreach (LanguageRange range in ranges)
                {
                    if (Offered(range.Language) is string matched)
                    {
                        return new LanguageRange(matched, range.Region);
                    }
                }
            }

            // --- 2. The stored language; 3. the tenant's primary; 4. the distribution default ---
            string language = Offered(input.PreferredLanguage)
                ?? Offered(settings.Languages.Count > 0 ? settings.Languages[0]?.Code : null)
                ?? _languages.Offered[0].Code;

            return new LanguageRange(language, null);
        }

        /// <summary>A surface's fixed message language, when it is offered.</summary>
        /// <param name="messageLanguage">The surface's value: a language, optionally with a region.</param>
        /// <returns>The language as the catalogue spells it and its region; an empty language when there is no override.</returns>
        private LanguageRange MessageLanguage(string? messageLanguage)
        {
            return !string.IsNullOrWhiteSpace(messageLanguage)
                && AcceptLanguage.TryReadTag(messageLanguage, out LanguageRange tag)
                && Offered(tag.Language) is string offered
                    ? new LanguageRange(offered, tag.Region)
                    : new LanguageRange(string.Empty, null);
        }

        /// <summary>An offered language as the catalogue spells it.</summary>
        /// <param name="code">A language code in any case.</param>
        /// <returns>The catalogue's spelling; null when the code is absent or not offered.</returns>
        private string? Offered(string? code)
        {
            return !string.IsNullOrEmpty(code) && _languages.IsOffered(code) ? _languages.Find(code)?.Code : null;
        }

        /// <summary>A region as the language's formats spell it.</summary>
        /// <param name="formats">The negotiated language's formats.</param>
        /// <param name="region">A region code in any case.</param>
        /// <returns>The region; null when it is absent or not offered with the language.</returns>
        private static string? Region(LanguageFormats formats, string? region)
        {
            if (string.IsNullOrEmpty(region))
            {
                return null;
            }

            foreach (string candidate in formats.Regions)
            {
                if (string.Equals(candidate, region, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The calendar dates render in.</summary>
        /// <param name="input">The negotiation input.</param>
        /// <param name="settings">The tenant's settings.</param>
        /// <returns>The calendar system.</returns>
        private ICalendarSystem Calendar(NegotiationInput input, TenantSettings settings)
        {
            // --- Override: any composed calendar, even outside the tenant's ---
            if (!string.IsNullOrWhiteSpace(input.MessageCalendar) && _calendars.TryGet(input.MessageCalendar.Trim()) is ICalendarSystem fixedCalendar)
            {
                return fixedCalendar;
            }

            // --- 1. The header, when it names one of the tenant's calendars ---
            if (!string.IsNullOrWhiteSpace(input.CalendarHeader))
            {
                if (TenantCalendar(settings, input.CalendarHeader.Trim()) is ICalendarSystem requested)
                {
                    return requested;
                }

                Reject(CalendarHeader);
            }

            // --- 2. The stored calendar, when it is still one of the tenant's; 3. the tenant's primary ---
            return TenantCalendar(settings, input.PreferredCalendar)
                ?? (settings.Calendars.Count > 0 ? settings.Calendars[0] : null)
                ?? _calendars.TryGet(CalendarCodes.Gregorian)
                ?? LastResortCalendar;
        }

        /// <summary>One of the tenant's calendars, by code.</summary>
        /// <param name="settings">The tenant's settings.</param>
        /// <param name="code">A calendar code in any case.</param>
        /// <returns>The system; null when the code is absent or not one of the tenant's calendars.</returns>
        private static ICalendarSystem? TenantCalendar(TenantSettings settings, string? code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            foreach (ICalendarSystem calendar in settings.Calendars)
            {
                if (string.Equals(calendar?.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return calendar;
                }
            }

            return null;
        }

        /// <summary>The zone instants render in.</summary>
        /// <param name="input">The negotiation input.</param>
        /// <param name="settings">The tenant's settings.</param>
        /// <returns>The display zone.</returns>
        private TimeZoneInfo TimeZone(NegotiationInput input, TenantSettings settings)
        {
            // --- 1. The header, when it is a value of the set ---
            if (!string.IsNullOrWhiteSpace(input.TimeZoneHeader))
            {
                if (_zones.TryGetValue(input.TimeZoneHeader.Trim(), out string? requested))
                {
                    // A value this host's tzdata cannot resolve is a fault of the host, which the
                    // startup warning reports; the header is valid, so nothing is counted.
                    if (TimeZoneInfo.TryFindSystemTimeZoneById(requested, out TimeZoneInfo? zone))
                    {
                        return zone;
                    }
                }
                else
                {
                    Reject(TimeZoneHeader);
                }
            }

            // --- 2. The stored zone, when it is still a value of the set; 3. the tenant's ---
            return !string.IsNullOrWhiteSpace(input.PreferredTimeZone)
                && _zones.TryGetValue(input.PreferredTimeZone.Trim(), out string? preferred)
                && TimeZoneInfo.TryFindSystemTimeZoneById(preferred, out TimeZoneInfo? stored)
                    ? stored
                    : settings.TimeZone;
        }

        /// <summary>Counts a request header that was present but invalid.</summary>
        /// <param name="header">The header's tag value.</param>
        private void Reject(string header)
        {
            _headersRejected.Add(1, new KeyValuePair<string, object?>(LocalizationTelemetryNames.HeaderTag, header));
        }
    }
}
