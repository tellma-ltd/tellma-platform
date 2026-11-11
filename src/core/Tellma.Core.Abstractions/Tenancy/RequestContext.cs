// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Who a unit of work runs for, against which tenant, and in which locale: one immutable
    ///     record per request or background scope, read through
    ///     <see cref="IRequestContextAccessor.Current" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The record is filled in stages. The platform binds the tenant and the principal
    ///         (<see cref="Tenant" />, <see cref="Kind" />, <see cref="Subject" />,
    ///         <see cref="ClientId" />, <see cref="SessionId" />, <see cref="SessionHandle" />,
    ///         <see cref="Assurance" />, <see cref="Now" />, <see cref="Client" />,
    ///         <see cref="OriginTraceParent" />) before anything else runs; then each
    ///         <see cref="IRequestContextInitializer" /> returns a copy with its own members set: the
    ///         user connection sets <see cref="UserId" />, and the locale negotiation sets the
    ///         language, culture, calendar, zones, <see cref="Today" /> and
    ///         <see cref="TenantSettings" />.
    ///     </para>
    ///     <para>
    ///         A copy is made with a <c>with</c> expression. Every member that has a rule refuses a
    ///         value that breaks it, so no copy can hold an empty language, a missing calendar or a
    ///         content-language position outside 1 to 3.
    ///     </para>
    /// </remarks>
    public sealed record RequestContext
    {
        /// <summary>The platform's default language and culture.</summary>
        private const string DefaultLanguage = "en";

        /// <summary>The platform's default zone.</summary>
        private const string DefaultTimeZone = "UTC";

        /// <summary>The default client name.</summary>
        private const string DefaultClient = "web";

        /// <summary>
        ///     The display zones resolved so far, by IANA name: each is resolved once per process, never
        ///     per request or per formatted value. Bounded by the zones the host knows.
        /// </summary>
        private static readonly ConcurrentDictionary<string, TimeZoneInfo> DisplayZones = new(StringComparer.Ordinal);

        /// <summary>The tenant the work is bound to; null for tenantless work.</summary>
        public TenantDescriptor? Tenant { get; init; }

        /// <summary>What kind of principal the work runs for.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a member of the enumeration.</exception>
        public PrincipalKind Kind
        {
            get;
            init => field = Enum.IsDefined(value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a principal kind.");
        }

        /// <summary>
        ///     The principal's stable identifier: a user's subject at the authority, a service
        ///     account's client id, or <c>system</c>. Never an email address. Null for an anonymous
        ///     principal.
        /// </summary>
        public string? Subject { get; init; }

        /// <summary>The client application the principal's token was issued to; null when there is none.</summary>
        public string? ClientId { get; init; }

        /// <summary>The authority's session id; null outside an interactive session.</summary>
        public string? SessionId { get; init; }

        /// <summary>
        ///     The handle of the caller's server-side session: the name that addresses one browser
        ///     session across tenants. Set only for a cookie-authenticated request; null on bearer
        ///     surfaces and in background scopes. It is never the session's key.
        /// </summary>
        public string? SessionHandle { get; init; }

        /// <summary>How strongly and how recently the session was authenticated; null when the principal has no session.</summary>
        public AuthenticationAssurance? Assurance { get; init; }

        /// <summary>
        ///     The principal's user in the bound tenant. Null until the user connection resolves it,
        ///     and for an anonymous principal; the platform's own work binds the tenant's system user.
        /// </summary>
        public int? UserId { get; init; }

        /// <summary>The language messages are written in: a BCP 47 tag without extensions.</summary>
        /// <exception cref="ArgumentException">The value is null, empty or white space.</exception>
        public string Language
        {
            get;
            init => field = NotBlank(value, nameof(Language));
        } = DefaultLanguage;

        /// <summary>The culture values are formatted in: a language and a region (<c>ar-SA</c>), without extensions.</summary>
        /// <exception cref="ArgumentException">The value is null, empty or white space.</exception>
        public string Culture
        {
            get;
            init => field = NotBlank(value, nameof(Culture));
        } = DefaultLanguage;

        /// <summary>
        ///     The formatting culture as an object: <see cref="Culture" /> with the Gregorian calendar
        ///     and Latin digits, whatever the culture's own defaults. Stored read-only, so no reader
        ///     can change the copy every other reader shares.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public CultureInfo CultureInfo
        {
            get;
            init => field = CultureInfo.ReadOnly(value ?? throw new ArgumentNullException(nameof(value)));
        } = CultureInfo.ReadOnly(CultureInfo.GetCultureInfo(DefaultLanguage));

        /// <summary>The code of the calendar dates render in: the code of <see cref="CalendarSystem" />.</summary>
        public string Calendar => CalendarSystem.Code;

        /// <summary>The calendar dates render in. Every context names one: there is no context without a calendar.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public required ICalendarSystem CalendarSystem
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        ///     The position (1 to 3) of the tenant content language that matches the culture, else 1:
        ///     which of a record's names names it for this reader.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is outside 1 to 3.</exception>
        public int ContentLanguageIndex
        {
            get;
            init => field = value is >= 1 and <= 3
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "A content-language position is 1, 2 or 3.");
        } = 1;

        /// <summary>
        ///     The display zone, an IANA name: the zone instants are formatted in for this reader. It
        ///     only formats; <see cref="TenantTimeZone" /> decides what today is.
        /// </summary>
        /// <exception cref="ArgumentException">The value is null, empty or white space.</exception>
        public string TimeZone
        {
            get;
            init => field = NotBlank(value, nameof(TimeZone));
        } = DefaultTimeZone;

        /// <summary>
        ///     The tenant's zone, an IANA name: the zone <see cref="Today" /> is computed in. No
        ///     caller can move it, so no caller can move a tenant's business date.
        /// </summary>
        /// <exception cref="ArgumentException">The value is null, empty or white space.</exception>
        public string TenantTimeZone
        {
            get;
            init => field = NotBlank(value, nameof(TenantTimeZone));
        } = DefaultTimeZone;

        /// <summary>The instant the unit of work started, fixed once from the host's clock.</summary>
        public DateTimeOffset Now { get; init; }

        /// <summary>The date of <see cref="Now" /> in <see cref="TenantTimeZone" />.</summary>
        public DateOnly Today { get; init; }

        /// <summary>The bound tenant's settings; null for tenantless work and until the locale negotiation loads them.</summary>
        public TenantSettings? TenantSettings { get; init; }

        /// <summary>The name of the client surface the work came through: <c>web</c>, <c>mcp</c> or <c>worker</c>.</summary>
        /// <exception cref="ArgumentException">The value is null, empty or white space.</exception>
        public string Client
        {
            get;
            init => field = NotBlank(value, nameof(Client));
        } = DefaultClient;

        /// <summary>
        ///     The trace parent of the request that enqueued a background unit of work. It joins the
        ///     work's log lines to that request and nothing more: no activity is started, parented or
        ///     linked from it.
        /// </summary>
        public string? OriginTraceParent { get; init; }

        /// <summary>Whether the bound tenant is a sandbox, whose work must cause no external side effect. False for tenantless work.</summary>
        public bool IsSandbox => Tenant?.Category == TenantCategory.Sandbox;

        /// <summary>Whether the work runs for the platform itself.</summary>
        public bool IsSystem => Kind == PrincipalKind.System;

        /// <summary>Whether the work runs for a service account.</summary>
        public bool IsServiceAccount => Kind == PrincipalKind.ServiceAccount;

        /// <summary>
        ///     The context of tenantless work: no tenant, an anonymous principal and the platform's
        ///     defaults (English, the calendar given, UTC, the <c>web</c> client).
        /// </summary>
        /// <param name="calendarSystem">The platform's default calendar system, the Gregorian one.</param>
        /// <param name="now">The instant the unit of work started; <see cref="Today" /> is its date in UTC.</param>
        /// <returns>The tenantless context.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="calendarSystem" /> is null.</exception>
        public static RequestContext Tenantless(ICalendarSystem calendarSystem, DateTimeOffset now)
        {
            return new RequestContext
            {
                CalendarSystem = calendarSystem,
                Now = now,
                Today = DateOnly.FromDateTime(now.UtcDateTime),
            };
        }

        /// <summary>
        ///     The localization text renders in for this unit of work: <see cref="CultureInfo" />,
        ///     <see cref="CalendarSystem" />, the display zone <see cref="TimeZone" /> resolved on the
        ///     host, and <see cref="ContentLanguageIndex" />.
        /// </summary>
        /// <remarks>
        ///     A display zone the host cannot resolve, which negotiation never yields because it
        ///     accepts only zones the host resolves, renders in UTC rather than failing the work.
        /// </remarks>
        public LocalizationContext Localization => new(CultureInfo, CalendarSystem, ResolveDisplayZone(TimeZone), ContentLanguageIndex);

        /// <summary>The tenant the work is bound to, for code that only runs inside a tenant.</summary>
        /// <returns>The bound tenant.</returns>
        /// <exception cref="InvalidOperationException">The work is not bound to a tenant.</exception>
        public TenantDescriptor RequireTenant()
        {
            return Tenant ?? throw new InvalidOperationException(
                "The unit of work is not bound to a tenant. Tenant-scoped code runs inside a tenant request or a scope the tenant scope factory created.");
        }

        /// <summary>
        ///     Makes <see cref="CultureInfo" /> the current culture and the current UI culture of the
        ///     calling flow, and <see cref="Localization" /> its request localization
        ///     (<see cref="LocalizationScope.Request" />), so text formatted and localized from here on
        ///     reads as the context says, dates in its calendar and display zone included. The context
        ///     stays the source of truth: the ambient values are set from it, never read back into it.
        /// </summary>
        /// <remarks>
        ///     The current cultures flow into what a method calls and awaits, never back to its caller:
        ///     whatever an asynchronous method sets is undone when it returns. So the method that runs
        ///     the work calls this itself, after the access guard or the tenant scope factory returned
        ///     the completed context: a tenant front door before it invokes the handler, a background
        ///     caller before it runs its work in the scope. A call made inside an
        ///     <see cref="IRequestContextInitializer" /> reaches neither. A caller that runs several
        ///     units of work on one flow applies each unit's context inside the method that runs that
        ///     unit, so no unit renders in another's localization.
        /// </remarks>
        public void ApplyCultures()
        {
            // --- The cultures: what resources are looked up and numbers formatted in ---
            CultureInfo.CurrentCulture = CultureInfo;
            CultureInfo.CurrentUICulture = CultureInfo;

            // --- The calendar and display zone dates render in outside every localization scope ---
            // Applied for the rest of the calling method, exactly as the cultures are; the undo
            // value is for callers that apply a request localization by hand.
            _ = LocalizationScope.ApplyRequest(Localization);
        }

        /// <summary>Resolves a display zone once per process.</summary>
        /// <param name="zone">The zone's IANA name.</param>
        /// <returns>The zone; UTC when the host does not know it.</returns>
        private static TimeZoneInfo ResolveDisplayZone(string zone)
        {
            return DisplayZones.GetOrAdd(
                zone,
                static id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? resolved) ? resolved : TimeZoneInfo.Utc);
        }

        /// <summary>Refuses a blank text member.</summary>
        /// <param name="value">The value.</param>
        /// <param name="member">The member the value is for.</param>
        /// <returns>The value.</returns>
        private static string NotBlank(string value, string member)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException($"A request context's {member} is never empty.", nameof(value))
                : value;
        }
    }
}
