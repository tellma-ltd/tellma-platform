// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     Labels by convention over the fixture model, whose entities one assembly declares while
    ///     their keys ship in this suite's own shared resources: every chain, in English and in
    ///     Arabic, each key-resolving twin, and the humanized names where no key is declared.
    /// </summary>
    /// <param name="composition">The fixture model, composed once for the class.</param>
    public sealed class LabelProviderTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>
    {
        /// <summary>The calendar every scope renders in; labels never read it.</summary>
        private static readonly GregorianCalendarSystem Gregorian = new();

        private readonly ServiceProvider _provider = composition.Services;

        /// <summary>The provider under test.</summary>
        private ILabelProvider Labels => _provider.GetRequiredService<ILabelProvider>();

        [Fact]
        public void An_entity_resolves_its_own_key_in_the_readers_language_and_its_plural_through_the_plural_key()
        {
            using (Scope("en"))
            {
                Assert.Equal("Contraption", Labels.EntityLabel(typeof(Widget)));
                Assert.Equal("Contraptions", Labels.EntityLabel(typeof(Widget), plural: true));
            }

            using (Scope("ar"))
            {
                Assert.Equal("أداة", Labels.EntityLabel(typeof(Widget)));
                Assert.Equal("أدوات", Labels.EntityLabel(typeof(Widget), plural: true));
            }

            Assert.Equal("Fixture_Widget", Labels.EntityLabelKey(typeof(Widget)));
            Assert.Equal("Fixture_Widget_Plural", Labels.EntityLabelKey(typeof(Widget), plural: true));
        }

        [Fact]
        public void An_entity_without_keys_reads_as_its_humanized_name_and_its_plural_as_its_singular()
        {
            using (Scope("ar"))
            {
                Assert.Equal("Widget part", Labels.EntityLabel(typeof(WidgetPart)));
                Assert.Equal("Widget part", Labels.EntityLabel(typeof(WidgetPart), plural: true));
            }

            Assert.Null(Labels.EntityLabelKey(typeof(WidgetPart)));
            Assert.Null(Labels.EntityLabelKey(typeof(WidgetPart), plural: true));
        }

        [Fact]
        public void A_property_resolves_its_entitys_key_then_the_shared_key_then_its_humanized_name()
        {
            using (Scope("ar"))
            {
                // The entity's own key; Core's shared key; neither.
                Assert.Equal("العنوان", Labels.PropertyLabel(typeof(Widget), "Name"));
                Assert.Equal("الرمز", Labels.PropertyLabel(typeof(Widget), "Code"));
                Assert.Equal("Due on", Labels.PropertyLabel(typeof(Widget), "DueOn"));
            }

            Assert.Equal("Fixture_Widget_Name", Labels.PropertyLabelKey(typeof(Widget), "Name"));
            Assert.Equal("Code", Labels.PropertyLabelKey(typeof(Widget), "Code"));
            Assert.Null(Labels.PropertyLabelKey(typeof(Widget), "DueOn"));
        }

        [Fact]
        public void A_key_a_language_lacks_renders_in_english()
        {
            using (Scope("ar"))
            {
                Assert.Equal("Components", Labels.CollectionLabel(typeof(Widget), "Parts"));
            }

            using (Scope("am"))
            {
                Assert.Equal("Contraption", Labels.EntityLabel(typeof(Widget)));
            }
        }

        [Fact]
        public void A_navigation_resolves_its_own_key_then_its_foreign_keys_chain_then_the_shared_key_then_its_humanized_name()
        {
            using (Scope("en"))
            {
                Assert.Equal("Parent node", Labels.NavigationLabel(typeof(Node), "Parent"));
                Assert.Equal("Counterpart", Labels.NavigationLabel(typeof(Link), "OtherNode"));
                Assert.Equal("Created by", Labels.NavigationLabel(typeof(Widget), "CreatedBy"));
                Assert.Equal("Parent", Labels.NavigationLabel(typeof(BlobFolder), "Parent"));
                Assert.Equal("Preferred supplier", Labels.NavigationLabel(typeof(Widget), "PreferredSupplier"));
            }

            // The audit column's navigation finds Core's shared key of its foreign key; a tree's
            // parent, with no key of its own or of its foreign key, finds Core's shared navigation key.
            Assert.Equal("Fixture_Node_Parent", Labels.NavigationLabelKey(typeof(Node), "Parent"));
            Assert.Equal("Fixture_Link_OtherNodeId", Labels.NavigationLabelKey(typeof(Link), "OtherNode"));
            Assert.Equal("CreatedById", Labels.NavigationLabelKey(typeof(Widget), "CreatedBy"));
            Assert.Equal("Parent", Labels.NavigationLabelKey(typeof(BlobFolder), "Parent"));
            Assert.Null(Labels.NavigationLabelKey(typeof(Widget), "PreferredSupplier"));
        }

        [Fact]
        public void A_collection_resolves_its_own_key_then_the_child_entitys_plural_label()
        {
            using (Scope("en"))
            {
                Assert.Equal("Components", Labels.CollectionLabel(typeof(Widget), "Parts"));
                Assert.Equal("Remarks", Labels.CollectionLabel(typeof(WidgetPart), "PartNotes"));
                Assert.Equal("Link", Labels.CollectionLabel(typeof(Node), "Links"));
                Assert.Equal("Attachments", Labels.CollectionLabel(typeof(Node), "Attachments"));
            }

            Assert.Equal("Fixture_Widget_Parts", Labels.CollectionLabelKey(typeof(Widget), "Parts"));
            Assert.Equal("Fixture_WidgetPartNote_Plural", Labels.CollectionLabelKey(typeof(WidgetPart), "PartNotes"));
            Assert.Null(Labels.CollectionLabelKey(typeof(Node), "Links"));
        }

        [Fact]
        public void An_action_resolves_its_entitys_key_then_the_shared_key_then_its_humanized_name()
        {
            using (Scope("ar"))
            {
                Assert.Equal("Seize", Labels.ActionLabel(typeof(Widget), "take-over"));
                Assert.Equal("تفعيل", Labels.ActionLabel(typeof(Widget), "activate"));
                Assert.Equal("Preferences set", Labels.ActionLabel(typeof(Widget), "preferences/set"));
            }

            Assert.Equal("Fixture_Widget_Action_TakeOver", Labels.ActionLabelKey(typeof(Widget), "take-over"));
            Assert.Equal("Action_Activate", Labels.ActionLabelKey(typeof(Widget), "Activate"));
            Assert.Null(Labels.ActionLabelKey(typeof(Widget), "preferences/set"));
        }

        [Fact]
        public void An_enum_value_resolves_under_the_schema_of_the_entities_that_carry_it()
        {
            using (Scope("ar"))
            {
                Assert.Equal("فاخر", Labels.EnumValueLabel(typeof(WidgetKind), "Fancy"));
                Assert.Equal("فاخر", Labels.EnumValueLabel(typeof(WidgetKind?), "Fancy"));
                Assert.Equal("Plain", Labels.EnumValueLabel(typeof(WidgetKind), "Plain"));

                // No entity carries it, so it has no schema and no key.
                Assert.Equal("Monday", Labels.EnumValueLabel(typeof(DayOfWeek), "Monday"));
            }

            Assert.Equal("Fixture_WidgetKind_Fancy", Labels.EnumValueLabelKey(typeof(WidgetKind), "Fancy"));
            Assert.Null(Labels.EnumValueLabelKey(typeof(WidgetKind), "Plain"));
            Assert.Null(Labels.EnumValueLabelKey(typeof(DayOfWeek), "Monday"));
            _ = Assert.Throws<ArgumentException>(() => Labels.EnumValueLabel(typeof(string), "Monday"));
        }

        [Fact]
        public void A_setting_resolves_its_label_key_then_its_names_humanized_last_segment()
        {
            SettingKey<bool> declared = new("fixture.widgets.autoNumber", false, category: "fixture");
            SettingKey<bool> undeclared = new("gl.posting.autoNumberOnPost", false, category: "gl");

            using (Scope("en"))
            {
                Assert.Equal("Number widgets automatically", Labels.SettingLabel(declared));
                Assert.Equal("Auto number on post", Labels.SettingLabel(undeclared));
            }

            Assert.Equal("Setting_fixture_widgets_autoNumber", Labels.SettingLabelKey(declared));
            Assert.Null(Labels.SettingLabelKey(undeclared));
        }

        [Fact]
        public void A_value_reads_as_its_keyed_label_its_literal_label_or_itself()
        {
            using (Scope("ar"))
            {
                Assert.Equal("أم القرى", Labels.ValueLabel(CoreValueSets.Calendar, "uq"));
                Assert.Equal("أم القرى", Labels.ValueLabel(CoreValueSets.Calendar, "UQ"));
                Assert.Equal("العربية", Labels.ValueLabel(CoreValueSets.Language, "ar"));
                Assert.Equal("xx", Labels.ValueLabel(CoreValueSets.Calendar, "xx"));
                Assert.Equal("uq", Labels.ValueLabel("acme.Nothing", "uq"));
            }

            Assert.Equal("Calendar_uq", Labels.ValueLabelKey(CoreValueSets.Calendar, "uq"));
            Assert.Null(Labels.ValueLabelKey(CoreValueSets.Language, "ar"));
            Assert.Null(Labels.ValueLabelKey(CoreValueSets.Calendar, "xx"));
            Assert.Null(Labels.ValueLabelKey("acme.Nothing", "uq"));
        }

        [Fact]
        public void An_entity_outside_the_model_is_refused()
        {
            _ = Assert.Throws<ArgumentException>(() => Labels.EntityLabel(typeof(string)));
            _ = Assert.Throws<ArgumentException>(() => Labels.PropertyLabelKey(typeof(string), "Length"));
        }

        [Fact]
        public void Outside_every_scope_a_label_reads_the_current_ui_culture_and_the_requests_content_languages()
        {
            // The request's localization, as a front door applies it, carries the tenant's languages.
            using IDisposable request = LocalizationScope.ApplyRequest(Context("ar", "en", "ar"));

            Assert.Equal($"Title ({Symbol("en")})", WithUiCulture("en-GB", () => Labels.PropertyLabel(typeof(Widget), "Name")));
            Assert.Equal($"العنوان ({Symbol("ar")})", WithUiCulture("ar-SA", () => Labels.PropertyLabel(typeof(Widget), "Name2")));
        }

        /// <summary>Enters a scope reading a language, for a tenant whose only content language is English.</summary>
        /// <param name="language">The reader's language.</param>
        /// <returns>The scope.</returns>
        private IDisposable Scope(string language)
        {
            return LocalizationScope.Enter(Context(language, "en"));
        }

        /// <summary>A localization reading a language for a tenant with given content languages.</summary>
        /// <param name="language">The reader's language.</param>
        /// <param name="contentLanguages">The tenant's content languages, in position order.</param>
        /// <returns>The localization.</returns>
        private LocalizationContext Context(string language, params string[] contentLanguages)
        {
            ILanguageCatalog catalog = _provider.GetRequiredService<ILanguageCatalog>();
            string region = catalog.Formats(language).DefaultRegion;
            return new LocalizationContext(
                NegotiatedCultures.Get(language, region),
                Gregorian,
                TimeZoneInfo.Utc,
                1,
                [.. contentLanguages.Select(code => catalog.Find(code)!)],
                language,
                language + "-" + region);
        }

        /// <summary>A language's symbol, as the catalogue gives it.</summary>
        /// <param name="code">The language.</param>
        /// <returns>The symbol.</returns>
        private string Symbol(string code)
        {
            return _provider.GetRequiredService<ILanguageCatalog>().Find(code)!.Symbol;
        }

        /// <summary>Runs a function under a current UI culture, restoring the previous one.</summary>
        /// <param name="culture">The culture's name.</param>
        /// <param name="function">The function.</param>
        /// <returns>What the function returns.</returns>
        private static string WithUiCulture(string culture, Func<string> function)
        {
            System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentUICulture;
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            try
            {
                return function();
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentUICulture = previous;
            }
        }
    }
}
