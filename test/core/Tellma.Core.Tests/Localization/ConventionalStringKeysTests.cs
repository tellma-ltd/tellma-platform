// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The keys a composed model derives by convention, over the fixture model: each family a
    ///     resource audit accepts a key of.
    /// </summary>
    /// <param name="composition">The fixture model, composed once for the class.</param>
    public sealed class ConventionalStringKeysTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>
    {
        private readonly ServiceProvider _provider = composition.Services;

        [Theory]
        // An entity's labels, and its members' with the shared fallbacks.
        [InlineData("Fixture_Widget")]
        [InlineData("Fixture_Widget_Plural")]
        [InlineData("Fixture_Widget_New")]
        [InlineData("Entity_New")]
        [InlineData("Fixture_Widget_Name")]
        [InlineData("Name")]
        [InlineData("Fixture_Widget_Parts")]
        [InlineData("Fixture_Node_Parent")]
        [InlineData("Parent")]
        [InlineData("Fixture_Link_OtherNode")]
        [InlineData("Fixture_WidgetKind_Fancy")]
        // Securables, the platform's actions, and the general settings' tab.
        [InlineData("Securable_core_UserStamp")]
        [InlineData("Core_User_Action_Read")]
        [InlineData("Action_Activate")]
        [InlineData("Feature_core")]
        [InlineData("SettingCategory_general")]
        // Notifications, channels, job handlers.
        [InlineData("Notification_fixture_shipment_shipped")]
        [InlineData("Notification_fixture_shipment_shipped_Detail")]
        [InlineData("NotificationType_fixture_shipment_held")]
        [InlineData("NotificationChannel_sms")]
        [InlineData("JobHandler_fixture_probe")]
        // Calendars, value sets, the brand.
        [InlineData("Calendar_et")]
        [InlineData("Calendar_et_Month13")]
        [InlineData("Region_SA")]
        [InlineData("Distribution_About")]
        public void Each_family_derives_its_keys_from_the_composed_model(string key)
        {
            Assert.Contains(key, ConventionalStringKeys.Of(_provider));
        }

        [Theory]
        [InlineData("Fixture_Widget_Name2")]
        [InlineData("Name3")]
        [InlineData("Calendar_uq_Month13")]
        [InlineData("Problem_not_found")]
        public void A_twin_a_month_a_calendar_lacks_and_a_code_of_the_source_are_not_derived(string key)
        {
            Assert.DoesNotContain(key, ConventionalStringKeys.Of(_provider));
        }
    }
}
