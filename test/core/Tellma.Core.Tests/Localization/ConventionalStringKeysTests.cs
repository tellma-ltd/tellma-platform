// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Localization;
using Tellma.Core.Queryex;

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
        // A collection falls back to its child entity's labels, never a bare key of its own.
        [InlineData("Parts")]
        // An entity's securable is labelled by the entity's own keys.
        [InlineData("Securable_fixture_Widget")]
        public void A_twin_a_month_a_calendar_lacks_a_code_of_the_source_and_a_key_no_lookup_reads_are_not_derived(string key)
        {
            Assert.DoesNotContain(key, ConventionalStringKeys.Of(_provider));
        }

        [Fact]
        public void Core_ships_the_shared_member_labels_whichever_entities_are_composed()
        {
            Assert.Superset(
                new HashSet<string> { "Name", "Code", "Description", "IsActive", "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy", "Parent" },
                ConventionalStringKeys.Of(_provider).ToHashSet());
        }

        [Fact]
        public void A_composed_setting_key_derives_its_label_description_group_and_category()
        {
            SettingKey<bool> key = new("fixture.widgets.autoNumber", false, category: "fixture", group: "widgets");
            SettingCategoryDescriptor category = new("fixture", "fixture.Settings.fixture", 1, "SettingCategory_fixture");
            HashSet<string> expected =
            [
                "Setting_fixture_widgets_autoNumber",
                "Setting_fixture_widgets_autoNumber_Description",
                "SettingGroup_fixture_widgets",
                "SettingCategory_fixture",
            ];

            AssertAdds(expected, new With(_provider, typeof(ISettingKeyRegistry), new OneSettingKey(key, category)));
        }

        [Theory]
        // No stated group: the middle segments name the card.
        [InlineData("fixture.gadgets.autoNumber", "SettingGroup_fixture_gadgets")]
        [InlineData("fixture.gadgets.posting.autoNumber", "SettingGroup_fixture_gadgets_posting")]
        // Two segments leave no middle, so the key sits on no card.
        [InlineData("fixture.limit", null)]
        public void An_ungrouped_setting_key_derives_the_group_its_name_names(string name, string? group)
        {
            SettingKey<bool> key = new(name, false, category: "fixture");
            SettingCategoryDescriptor category = new("fixture", "fixture.Settings.fixture", 1, "SettingCategory_fixture");
            string label = "Setting_" + name.Replace('.', '_');
            HashSet<string> expected = [label, label + "_Description", "SettingCategory_fixture"];
            if (group is not null)
            {
                _ = expected.Add(group);
            }

            AssertAdds(expected, new With(_provider, typeof(ISettingKeyRegistry), new OneSettingKey(key, category)));
        }

        [Fact]
        public void A_composed_stack_action_derives_its_labels_and_its_argument_fields_labels()
        {
            EntityMetadata widget = _provider.GetRequiredService<IEntityMetadataProvider>().All.Single(static entity => entity.Name == "fixture.Widget");
            HashSet<string> expected =
            [
                "Fixture_Widget_Action_Reassign",
                "Action_Reassign",
                "Fixture_Widget_Action_Reassign_Assignee",
                "Fixture_Widget_Assignee",
                "Assignee",
            ];

            AssertAdds(expected, new With(_provider, typeof(IStackRegistry), new OneStack(Stack(widget, "reassign", typeof(ReassignArguments)))));
        }

        /// <summary>
        ///     Asserts that the conventional keys of a composition with one more registry are those of
        ///     the fixture composition plus exactly the expected keys.
        /// </summary>
        /// <param name="expected">The keys the registry adds.</param>
        /// <param name="services">The fixture composition's services with the registry.</param>
        private void AssertAdds(HashSet<string> expected, IServiceProvider services)
        {
            IReadOnlySet<string> without = ConventionalStringKeys.Of(_provider);
            IReadOnlySet<string> with = ConventionalStringKeys.Of(services);

            // Every expected key is derived, and nothing else is new.
            Assert.Superset(expected, with.ToHashSet());
            Assert.Subset(expected, with.Except(without).ToHashSet());
        }

        /// <summary>A stack over an entity with one entity action.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="arguments">The action's arguments type.</param>
        /// <returns>The stack.</returns>
        private static StackDescriptor Stack(EntityMetadata entity, string action, Type arguments)
        {
            EntityActionDescriptor descriptor = new(
                action, null, Mutation: true, AllowWhenReadOnly: false, Idempotent: false, Long: false, Destructive: false, Mcp: default,
                ContractRevision: 1, typeof(void), action, SupportsFilter: false, LoadChildren: false, PostCheck: false,
                SingleTarget: false, IsBuiltIn: false, ArgumentsType: arguments);

            return new StackDescriptor(
                entity.Name, "widgets", entity.ClrType, typeof(object), typeof(int), null, default, new HashSet<string>(), [descriptor],
                entity.Properties, [], [], [], "Id", new HashSet<string>(), [], null, entity.DisplayName, [],
                new Dictionary<string, FilterTree>(), StackLimits.Default, default, 1, "fixture");
        }

        /// <summary>An action's arguments, whose one field carries the argument labels.</summary>
        /// <param name="Assignee">The field.</param>
        private sealed record ReassignArguments(string Assignee);

        /// <summary>The fixture composition's services, with one more service answered.</summary>
        /// <param name="inner">The fixture composition's services.</param>
        /// <param name="type">The added service's type.</param>
        /// <param name="service">The added service.</param>
        private sealed class With(IServiceProvider inner, Type type, object service) : IServiceProvider
        {
            /// <inheritdoc />
            public object? GetService(Type serviceType)
            {
                return serviceType == type ? service : inner.GetService(serviceType);
            }
        }

        /// <summary>A setting-key registry of given keys and categories.</summary>
        /// <param name="key">The one key.</param>
        /// <param name="category">The one category.</param>
        private sealed class OneSettingKey(SettingKey key, SettingCategoryDescriptor category) : ISettingKeyRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<SettingKey> Keys { get; } = [key];

            /// <inheritdoc />
            public IReadOnlyList<SettingCategoryDescriptor> Categories { get; } = [category];

            /// <inheritdoc />
            public SettingKey? Find(string name)
            {
                return string.Equals(name, key.Name, StringComparison.Ordinal) ? key : null;
            }

            /// <inheritdoc />
            public IReadOnlyList<SettingKeyDescriptor> Describe()
            {
                return [];
            }
        }

        /// <summary>A stack registry of one stack.</summary>
        /// <param name="stack">The stack.</param>
        private sealed class OneStack(StackDescriptor stack) : IStackRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<StackDescriptor> Stacks { get; } = [stack];

            /// <inheritdoc />
            public IReadOnlyList<ChildQueryDescriptor> ChildQueries { get; } = [];

            /// <inheritdoc />
            public IReadOnlyList<ApiServiceDescriptor> Services { get; } = [];

            /// <inheritdoc />
            public IReadOnlyList<EnlistmentDescriptor> Enlistments { get; } = [];

            /// <inheritdoc />
            public StackDescriptor? Find(string resource)
            {
                return string.Equals(resource, stack.Resource, StringComparison.OrdinalIgnoreCase) ? stack : null;
            }

            /// <inheritdoc />
            public ChildQueryDescriptor? FindChildQuery(string resource)
            {
                return null;
            }

            /// <inheritdoc />
            public StackDescriptor? OwnerOf(TableName table)
            {
                return null;
            }
        }
    }
}
