// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     A multilingual group over the fixture model: its twins found by name, each labelled with
    ///     its content language's symbol in the tenant's shape, and offered exactly where the shape's
    ///     query schema admits it.
    /// </summary>
    /// <param name="composition">The fixture model, composed once for the class.</param>
    public sealed class MultilingualLabelsTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>
    {
        /// <summary>The tenant's content languages, in position order; a shape takes the first one, two or three.</summary>
        private static readonly string[] ContentLanguages = FixtureLabelsComposition.Languages;

        /// <summary>The group's columns, in position order.</summary>
        private static readonly string[] Columns = ["Name", "Name2", "Name3"];

        private readonly ServiceProvider _provider = composition.Services;

        [Fact]
        public void The_twins_are_found_by_name_beside_their_primary()
        {
            EntityMetadata widget = _provider.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Widget));

            MultilingualGroup group = Assert.Single(widget.MultilingualGroups);
            Assert.Equal(Columns, new[] { group.Primary.Name, group.Secondary?.Name, group.Tertiary?.Name });
        }

        [Theory]
        [InlineData(MultilingualShape.Primary)]
        [InlineData(MultilingualShape.PrimaryAndSecondary)]
        [InlineData(MultilingualShape.All)]
        public void Each_shape_labels_the_columns_its_schema_admits_with_their_languages_symbols(MultilingualShape shape)
        {
            // --- The shape's schema admits the first one, two or three columns ---
            ILanguageCatalog catalog = _provider.GetRequiredService<ILanguageCatalog>();
            LanguageInfo[] languages = [.. ContentLanguages.Take((int)shape).Select(code => catalog.Find(code)!)];
            HashSet<string> admitted = [.. _provider.GetRequiredService<IQueryexSchemaProvider>().GetSchema(shape)
                .FindEntity("fixture.Widget")!.Properties.Select(static property => property.Name)];
            string[] offered = [.. Columns.Where(admitted.Contains)];
            Assert.Equal(Columns.Take((int)shape), offered);

            // --- Each admitted column reads as the primary's label, suffixed once there are two languages ---
            ILabelProvider labels = _provider.GetRequiredService<ILabelProvider>();
            using IDisposable scope = LocalizationScope.Enter(new LocalizationContext(
                NegotiatedCultures.Get("en", "GB"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, languages, "en", "en-GB"));
            string[] expected = shape == MultilingualShape.Primary
                ? ["Title"]
                : [.. languages.Select(static language => $"Title ({language.Symbol})")];
            Assert.Equal(expected, offered.Select(column => labels.PropertyLabel(typeof(Widget), column)));

            // --- Every column resolves through the primary's key, whatever the shape ---
            Assert.All(Columns, column => Assert.Equal("Fixture_Widget_Name", labels.PropertyLabelKey(typeof(Widget), column)));
        }

        [Fact]
        public void The_symbols_are_the_catalogues()
        {
            ILabelProvider labels = _provider.GetRequiredService<ILabelProvider>();
            ILanguageCatalog catalog = _provider.GetRequiredService<ILanguageCatalog>();
            using IDisposable scope = LocalizationScope.Enter(new LocalizationContext(
                NegotiatedCultures.Get("en", "GB"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, [catalog.Find("en")!, catalog.Find("ar")!], "en", "en-GB"));

            Assert.Equal("Title (E)", labels.PropertyLabel(typeof(Widget), "Name"));
            Assert.Equal("Title (ع)", labels.PropertyLabel(typeof(Widget), "Name2"));
        }

        [Fact]
        public void A_column_beyond_the_tenants_languages_reads_as_its_name()
        {
            ILabelProvider labels = _provider.GetRequiredService<ILabelProvider>();
            ILanguageCatalog catalog = _provider.GetRequiredService<ILanguageCatalog>();
            using IDisposable scope = LocalizationScope.Enter(new LocalizationContext(
                NegotiatedCultures.Get("en", "GB"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, [catalog.Find("en")!, catalog.Find("ar")!], "en", "en-GB"));

            // No shape offers it, so no caller asks; were one to, it gets the column's own name.
            Assert.Equal("Name3", labels.PropertyLabel(typeof(Widget), "Name3"));
        }
    }
}
