// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Settings;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Tests.Settings
{
    /// <summary>
    ///     The row the settings provisioning step completes the placeholder with: the seeded
    ///     languages in order with the names that go with them, the seeded calendars, and the seeds
    ///     the settings editor would refuse.
    /// </summary>
    public sealed class SettingsProvisioningStepTests : IAsyncDisposable
    {
        /// <summary>The tenant every case completes.</summary>
        private static readonly TenantDescriptor Tenant = new(3, "Acme", "أكمي", "Acmé", ["en"], TenantCategory.Live, TenantState.Active, null);

        /// <summary>The placeholder's stamp.</summary>
        private static readonly DateTimeOffset Stamp = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>A composition offering four languages, so a seed can list one too many.</summary>
        private readonly ServiceProvider provider = TestComposition.Compose(static tellma => tellma.Languages(["en", "ar", "fr", "am"]))
            .BuildServiceProvider(validateScopes: true);

        /// <summary>Seeds the settings editor would refuse: the languages, the calendars and what is wrong.</summary>
        public static TheoryData<string[], string[], string> Refused => new()
        {
            { ["en", "en"], [], "lists the language 'en' twice" },
            { ["en", "ar", "fr", "am"], [], "lists 4 languages; a tenant has at most 3" },
            { ["zz"], [], "lists the language 'zz', which the distribution does not offer" },
            { [], ["uq", "uq"], "lists the calendar 'uq' twice" },
            { [], ["gc", "uq", "et"], "lists 3 calendars; a tenant has at most 2" },
            { [], ["xx"], "lists the calendar 'xx', which no composed feature registers" },
        };

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return provider.DisposeAsync();
        }

        [Fact]
        public void An_unseeded_tenant_gets_the_distributions_first_language_the_gregorian_calendar_and_utc()
        {
            TypedSettings row = Step().Complete(Tenant, Stamp);

            Assert.Equal((3, "Acme", null, null), (row.TenantId, row.Name, row.Name2, row.Name3));
            Assert.Equal(("en", null, null), (row.PrimaryLanguage, row.SecondaryLanguage, row.TertiaryLanguage));
            Assert.Equal(("gc", null, "UTC"), (row.PrimaryCalendar, row.SecondaryCalendar, row.TimeZone));
            Assert.Equal((TypedSettings.SingleRowId, Stamp), (row.Id, row.ModifiedAt));
        }

        [Fact]
        public void A_seeded_tenant_gets_its_languages_in_order_with_their_names_and_its_calendars()
        {
            TypedSettings two = Step(Seed(["en", "ar"], ["gc", "uq"])).Complete(Tenant, Stamp);
            Assert.Equal(("en", "ar", null), (two.PrimaryLanguage, two.SecondaryLanguage, two.TertiaryLanguage));
            Assert.Equal(("Acme", "أكمي", null), (two.Name, two.Name2, two.Name3));
            Assert.Equal(("gc", "uq"), (two.PrimaryCalendar, two.SecondaryCalendar));

            // The codes are trimmed, and the primary is the seed's first, whichever it is.
            TypedSettings three = Step(Seed([" ar", "fr ", "en"], ["uq"])).Complete(Tenant, Stamp);
            Assert.Equal(("ar", "fr", "en"), (three.PrimaryLanguage, three.SecondaryLanguage, three.TertiaryLanguage));
            Assert.Equal(("Acme", "أكمي", "Acmé"), (three.Name, three.Name2, three.Name3));
            Assert.Equal(("uq", null), (three.PrimaryCalendar, three.SecondaryCalendar));
        }

        [Theory]
        [MemberData(nameof(Refused))]
        public void A_seed_the_settings_editor_would_refuse_fails_the_step(string[] languages, string[] calendars, string problem)
        {
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Step(Seed(languages, calendars)).Complete(Tenant, Stamp));

            Assert.Contains($"The seed of tenant 3 (Tellma:Seed:Tenants) {problem}.", refused.Message, StringComparison.Ordinal);
        }

        /// <summary>The step over the composition and a seed.</summary>
        /// <param name="seed">The seed; none when null.</param>
        /// <returns>The step.</returns>
        private SettingsProvisioningStep Step(TellmaSeedOptions? seed = null)
        {
            return new SettingsProvisioningStep(
                provider.GetRequiredService<ILanguageCatalog>(),
                provider.GetRequiredService<ICalendarRegistry>(),
                Options.Create(seed ?? new TellmaSeedOptions()));
        }

        /// <summary>A seed listing the tenant's languages and calendars.</summary>
        /// <param name="languages">The languages.</param>
        /// <param name="calendars">The calendars.</param>
        /// <returns>The seed.</returns>
        private static TellmaSeedOptions Seed(string[] languages, string[] calendars)
        {
            TellmaSeedTenant tenant = new() { Id = Tenant.Id, Name = Tenant.Name };
            foreach (string language in languages)
            {
                tenant.Languages.Add(language);
            }

            foreach (string calendar in calendars)
            {
                tenant.Calendars.Add(calendar);
            }

            TellmaSeedOptions seed = new();
            seed.Tenants.Add(tenant);
            return seed;
        }
    }
}
