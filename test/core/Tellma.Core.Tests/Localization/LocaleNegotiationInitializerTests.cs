// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Tenancy;
using Tellma.Core.Testing.Settings;
using Tellma.Core.Tests.Tenancy;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The initializer that completes a tenant scope's context with the tenant's settings and the
    ///     negotiated locale, after the user connection: the request's inputs first, then the
    ///     connected user's stored preferences, then the tenant's settings.
    /// </summary>
    public class LocaleNegotiationInitializerTests
    {
        /// <summary>An evening instant in UTC that is already the next day in Riyadh.</summary>
        private static readonly DateTimeOffset Evening = new(2026, 3, 14, 22, 30, 0, TimeSpan.Zero);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task It_runs_after_the_user_connection()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();

            IRequestContextInitializer initializer = Assert.Single(scope.ServiceProvider.GetServices<IRequestContextInitializer>());
            Assert.Equal(200, initializer.Order);
        }

        [Fact]
        public async Task Tenantless_work_keeps_its_context()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.Services.CreateAsyncScope();
            RequestContext context = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current;

            RequestContext completed = await Initializer(scope, Riyadh).InitializeAsync(context, RequestContextInputs.Empty, Token);

            Assert.Same(context, completed);
        }

        [Fact]
        public async Task With_nothing_asked_the_tenants_settings_decide_and_today_is_the_tenants()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());
            RequestContext bound = Current(scope) with { Now = Evening };

            RequestContext completed = await Initializer(scope, Riyadh).InitializeAsync(bound, RequestContextInputs.Empty, Token);

            Assert.Equal(TenantContextHarness.Tenant().Id, completed.TenantSettings?.TenantId);
            Assert.Equal("Asia/Riyadh", completed.TenantSettings?.TimeZone.Id);
            Assert.Equal(("en", "en-GB", "gc"), (completed.Language, completed.Culture, completed.CalendarSystem.Code));
            Assert.Equal("en-GB", completed.CultureInfo.Name);
            Assert.Equal(("Asia/Riyadh", "Asia/Riyadh"), (completed.TimeZone, completed.TenantTimeZone));
            Assert.Equal(new DateOnly(2026, 3, 15), completed.Today);
            Assert.Equal(1, completed.ContentLanguageIndex);

            // --- What the earlier steps bound is kept ---
            Assert.Equal((bound.Tenant, bound.Kind, bound.Subject, bound.Now), (completed.Tenant, completed.Kind, completed.Subject, completed.Now));
        }

        [Fact]
        public async Task The_requested_zone_is_the_display_zone_while_today_stays_the_tenants()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            RequestContext completed = await Initializer(scope, Utc).InitializeAsync(
                Current(scope) with { Now = Evening }, new RequestContextInputs(null, null, "Asia/Riyadh", "web", null, null), Token);

            Assert.Equal(("Asia/Riyadh", "UTC"), (completed.TimeZone, completed.TenantTimeZone));
            Assert.Equal(new DateOnly(2026, 3, 14), completed.Today);
        }

        [Fact]
        public async Task Today_is_the_date_of_the_scopes_own_instant_whatever_the_clock_reads_by_then()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());
            DateTimeOffset beforeMidnight = new(2026, 3, 14, 20, 59, 59, 900, TimeSpan.Zero);

            // The scope was stamped just before midnight in Riyadh; the clock has long moved on (to
            // November), as it moves on past midnight while a cold settings load runs.
            RequestContext completed = await Initializer(scope, Riyadh).InitializeAsync(
                Current(scope) with { Now = beforeMidnight }, RequestContextInputs.Empty, Token);

            Assert.Equal(new DateOnly(2026, 3, 14), completed.Today);
            Assert.Equal(beforeMidnight, completed.Now);
        }

        [Fact]
        public async Task The_language_and_culture_are_the_negotiated_pair_even_when_the_host_lacks_its_culture()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            // A host without the pair's culture data resolves it to the invariant culture, whose name is empty.
            InvariantNegotiator negotiator = new("zh-Hant", "TW");
            RequestContext completed = await Initializer(scope, Utc, negotiator: negotiator).InitializeAsync(Current(scope), RequestContextInputs.Empty, Token);

            Assert.Equal(("zh-Hant", "zh-Hant-TW"), (completed.Language, completed.Culture));
            Assert.Equal(string.Empty, completed.CultureInfo.Name);
        }

        [Fact]
        public async Task The_connected_users_stored_preferences_come_before_the_tenants()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());
            StoredPreferences preferences = new(new UserLocalePreferences(null, null, "Africa/Addis_Ababa", null));

            RequestContext completed = await Initializer(scope, Utc, preferences).InitializeAsync(
                Current(scope) with { UserId = 5 }, RequestContextInputs.Empty, Token);

            Assert.Equal("Africa/Addis_Ababa", completed.TimeZone);
            Assert.Equal([5], preferences.Asked);
        }

        [Fact]
        public async Task A_scope_no_user_is_connected_to_reads_no_preferences()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(), PrincipalKind.System, "system");
            StoredPreferences preferences = new(new UserLocalePreferences(null, null, "Africa/Addis_Ababa", null));

            RequestContext completed = await Initializer(scope, Utc, preferences).InitializeAsync(Current(scope), RequestContextInputs.Empty, Token);

            Assert.Equal("UTC", completed.TimeZone);
            Assert.Empty(preferences.Asked);
        }

        [Fact]
        public async Task The_composed_initializer_completes_a_connected_scope_and_sets_no_ambient_culture()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddConnectingInitializer()
                .UseFixedTenantSettings(static settings => settings with { TimeZone = Riyadh }));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());
            CultureInfo ambient = CultureInfo.CurrentCulture;

            await scope.ServiceProvider.GetRequiredService<ITenantAccessGuard>().EnsureAccessAsync(new TenantAccessRequirement(false, null), RequestContextInputs.Empty, Token);

            RequestContext completed = Current(scope);
            Assert.Equal(InitializerRegistrations.UserId, completed.UserId);
            Assert.NotNull(completed.TenantSettings);
            Assert.Equal("Asia/Riyadh", completed.TenantTimeZone);
            Assert.Same(ambient, CultureInfo.CurrentCulture);
        }

        /// <summary>Riyadh's zone.</summary>
        private static TimeZoneInfo Riyadh => TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

        /// <summary>UTC.</summary>
        private static TimeZoneInfo Utc => TimeZoneInfo.Utc;

        /// <summary>The initializer over fixed settings in a zone.</summary>
        /// <param name="scope">The scope whose tenant the settings are for.</param>
        /// <param name="zone">The tenant's zone.</param>
        /// <param name="preferences">The stored preferences; none when null.</param>
        /// <param name="negotiator">The negotiator; the composed one when null.</param>
        /// <returns>The initializer.</returns>
        private static LocaleNegotiationInitializer Initializer(
            AsyncServiceScope scope,
            TimeZoneInfo zone,
            IUserLocalePreferences? preferences = null,
            ILocalizationNegotiator? negotiator = null)
        {
            IServiceProvider services = scope.ServiceProvider;
            FixedTenantSettingsCache settings = new(
                services.GetRequiredService<IRequestContextAccessor>(),
                services.GetRequiredService<ILanguageCatalog>(),
                services.GetRequiredService<ICalendarRegistry>(),
                new FixedTenantSettingsOptions { Configure = settings => settings with { TimeZone = zone } });
            return new LocaleNegotiationInitializer(settings, negotiator ?? services.GetRequiredService<ILocalizationNegotiator>(), preferences);
        }

        /// <summary>The scope's current context.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The context.</returns>
        private static RequestContext Current(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current;
        }

        /// <summary>A negotiator that always settles on one pair, resolved as a host without its culture data does.</summary>
        /// <param name="language">The negotiated language.</param>
        /// <param name="region">The negotiated region.</param>
        private sealed class InvariantNegotiator(string language, string region) : ILocalizationNegotiator
        {
            /// <inheritdoc />
            public LocalizationContext Negotiate(NegotiationInput input)
            {
                ICalendarSystem gregorian = new GregorianCalendarSystem();
                return new LocalizationContext(CultureInfo.InvariantCulture, gregorian, TimeZoneInfo.Utc, 1, input.Settings.Languages, language, language + "-" + region);
            }
        }

        /// <summary>Stored preferences that answer every user alike and remember who was asked.</summary>
        /// <param name="stored">The preferences.</param>
        private sealed class StoredPreferences(UserLocalePreferences stored) : IUserLocalePreferences
        {
            /// <summary>The users asked about, in order.</summary>
            public List<int> Asked { get; } = [];

            /// <inheritdoc />
            public ValueTask<UserLocalePreferences?> GetAsync(int userId, CancellationToken cancellationToken)
            {
                Asked.Add(userId);
                return ValueTask.FromResult<UserLocalePreferences?>(stored);
            }
        }
    }
}
