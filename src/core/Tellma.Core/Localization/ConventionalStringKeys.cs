// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Access;
using Tellma.Core.Client;
using Tellma.Core.Jobs;
using Tellma.Core.Settings;

namespace Tellma.Core.Localization
{
    // The families are spec 0013 §10.1's key conventions and the label chains of §7.6.
    /// <summary>
    ///     Every shared string resource key a realised composition's model gives a meaning to by
    ///     convention: the keys its labels, settings, securables, notifications, job handlers,
    ///     calendars, value sets and brand are looked up by. A resource audit checks a feature's
    ///     resources against them, so a key nothing would ever look up is found before it ships.
    /// </summary>
    /// <remarks>
    ///     <para>The families, each from the registry that declares its subjects:</para>
    ///     <list type="bullet">
    ///         <item>
    ///             each entity's <c>&lt;Schema&gt;_&lt;Entity&gt;</c>, <c>…_Plural</c> and <c>…_New</c>;
    ///             each property's, navigation's and child collection's <c>…_&lt;Member&gt;</c>; each
    ///             property's and navigation's shared <c>&lt;Member&gt;</c> (a collection's label
    ///             falls back to its child entity's, and a multilingual group's twins take their
    ///             primary's label, so neither has one); the shared member labels Core ships
    ///             (<c>Name</c>, <c>Parent</c>, …) whichever entities are composed; and
    ///             <c>Entity_New</c>;
    ///         </item>
    ///         <item>
    ///             each C# enum member's <c>&lt;Schema&gt;_&lt;Enum&gt;_&lt;Value&gt;</c>, under the
    ///             schema of every entity, setting key and stack whose properties or action
    ///             arguments carry the enum;
    ///         </item>
    ///         <item>
    ///             each securable's resource label by the chain the client metadata looks it up by
    ///             (an entity's own labels, a settings category's <c>SettingCategory_&lt;category&gt;</c>,
    ///             else <c>Securable_&lt;resource&gt;</c>), its action's
    ///             <c>&lt;Resource&gt;_Action_&lt;Name&gt;</c> and <c>Action_&lt;Name&gt;</c>, and its
    ///             feature's <c>Feature_&lt;feature&gt;</c>; and <c>Action_&lt;Name&gt;</c> of each of
    ///             the platform's own actions, which any resource may register;
    ///         </item>
    ///         <item>
    ///             with stacks composed, the keys of each action of a stack or a child query surface
    ///             and those of its argument fields, <c>&lt;Resource&gt;_Action_&lt;Name&gt;_&lt;Field&gt;</c>,
    ///             <c>&lt;Resource&gt;_&lt;Field&gt;</c> and <c>&lt;Field&gt;</c>, where a field is
    ///             named by its C# property name, less the trailing <c>Id</c> of an entity reference;
    ///         </item>
    ///         <item>
    ///             with setting keys composed, each key's label and <c>…_Description</c>, its group's
    ///             <c>SettingGroup_&lt;category&gt;_&lt;group&gt;</c> and each category's label;
    ///             <c>SettingCategory_general</c> always;
    ///         </item>
    ///         <item>
    ///             each notification type's <c>Notification_&lt;key&gt;</c>, <c>…_Detail</c> and
    ///             <c>NotificationType_&lt;key&gt;</c>; each notification category's
    ///             <c>NotificationCategory_&lt;category&gt;</c>; each channel's
    ///             <c>NotificationChannel_&lt;key&gt;</c>; each job handler's <c>JobHandler_&lt;key&gt;</c>;
    ///         </item>
    ///         <item>
    ///             each calendar's <c>Calendar_&lt;code&gt;</c> and month names
    ///             <c>Calendar_&lt;code&gt;_Month&lt;n&gt;</c>; each value-set entry's label key;
    ///             and the distribution's <c>Distribution_DisplayName</c>,
    ///             <c>Distribution_Copyright</c>, <c>Distribution_ShortName</c> and
    ///             <c>Distribution_About</c>.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Codes the platform raises from code (problem codes, validation codes, query
    ///         diagnostics) are not here: their keys follow string literals of the source, which an
    ///         audit reads from the source itself.
    ///     </para>
    /// </remarks>
    public static class ConventionalStringKeys
    {
        /// <summary>The category of the general settings, whose label Core ships.</summary>
        private const string GeneralCategory = "general";

        /// <summary>The distribution's optional brand keys.</summary>
        private static readonly string[] DistributionKeys =
        [
            "Distribution_DisplayName",
            "Distribution_Copyright",
            "Distribution_ShortName",
            "Distribution_About",
        ];

        /// <summary>Every conventional key of a realised composition's model.</summary>
        /// <param name="services">The realised composition's services.</param>
        /// <returns>The keys, compared ordinally.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
        public static IReadOnlySet<string> Of(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            HashSet<string> keys = new(StringComparer.Ordinal);
            IEntityMetadataProvider metadata = services.GetRequiredService<IEntityMetadataProvider>();
            IStackRegistry? stacks = services.GetService<IStackRegistry>();
            ISettingKeyRegistry? settings = services.GetService<ISettingKeyRegistry>();
            AddEntities(keys, metadata);
            AddEnums(keys, NamedEnums.Discover(metadata, settings?.Keys ?? [], stacks));
            AddSecurables(keys, services.GetRequiredService<ISecurableRegistry>(), metadata);
            AddStacks(keys, stacks);
            AddSettings(keys, settings);
            AddNotifications(keys, services.GetRequiredService<INotificationTypeRegistry>(), services.GetRequiredService<INotificationChannelRegistry>());
            AddJobHandlers(keys, services.GetService<JobHandlerRegistry>());
            AddCalendars(keys, services.GetRequiredService<ICalendarRegistry>());
            AddValueSets(keys, services.GetRequiredService<IValueSetRegistry>());
            keys.UnionWith(DistributionKeys);
            return keys;
        }

        /// <summary>The entities' labels and their members' labels with the shared fallbacks.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        private static void AddEntities(HashSet<string> keys, IEntityMetadataProvider metadata)
        {
            _ = keys.Add(StringKeyConventions.EntityNew);
            keys.UnionWith(StringKeyConventions.SharedMemberLabels);
            foreach (EntityMetadata entity in metadata.All)
            {
                // --- The entity: singular, plural, create ---
                string prefix = StringKeyConventions.KeyPrefix(entity.Name);
                keys.UnionWith([.. LabelChains.Entity(entity.Name), .. LabelChains.Plural(entity.Name), .. LabelChains.New(entity.Name)]);

                // --- Its members: each with its own key; a property and a navigation also with the
                // shared fallback, while a collection falls back to its child entity's labels ---
                // A multilingual group's twins take the primary's label, so they have no key of their own.
                HashSet<string> twins = [.. entity.MultilingualGroups
                    .SelectMany(static group => new[] { group.Secondary?.Name, group.Tertiary?.Name })
                    .OfType<string>()];

                IEnumerable<string> members = entity.Properties.Select(static property => property.Name).Where(name => !twins.Contains(name))
                    .Concat(entity.References.Select(static reference => reference.Navigation));
                foreach (string member in members)
                {
                    keys.UnionWith([$"{prefix}_{member}", member]);
                }

                keys.UnionWith(entity.Children.Select(child => $"{prefix}_{child.Property}"));
            }
        }

        /// <summary>The C# enums' values, under every schema that carries each enum.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="enums">The composition's C# enums.</param>
        private static void AddEnums(HashSet<string> keys, IReadOnlyList<NamedEnum> enums)
        {
            foreach (NamedEnum named in enums)
            {
                keys.UnionWith(Enum.GetNames(named.Type).SelectMany(value => LabelChains.EnumValue(named.KeyPrefixes, named.Type.Name, value)));
            }
        }

        /// <summary>The securables' resource, action and feature labels.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The securable registry.</param>
        /// <param name="metadata">The model's entity metadata, which says which resources are entities.</param>
        private static void AddSecurables(HashSet<string> keys, ISecurableRegistry registry, IEntityMetadataProvider metadata)
        {
            // The platform's actions share Core's labels, whichever resources register them.
            keys.UnionWith(SecurableRegistry.PlatformActionOrder.Select(static action => StringKeyConventions.ActionPrefix + StringKeyConventions.ActionSegment(action)));
            foreach (SecurableDescriptor securable in registry.All)
            {
                // The resource's label by the client metadata's own chain, so a key it never looks
                // up (an entity's or a settings category's Securable_ key) is never derived.
                keys.UnionWith(
                [
                    .. SecurablesClientAssetContributor.ResourceLabelChain(securable.Resource, metadata).Chain,
                    StringKeyConventions.FromDottedKey("Feature_", securable.Feature),
                    .. ActionKeys(securable.Resource, securable.Action),
                ]);
            }
        }

        /// <summary>The action labels of the stacks and the child query surfaces, and their argument fields' labels.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The stack registry; null when no stack is composed.</param>
        private static void AddStacks(HashSet<string> keys, IStackRegistry? registry)
        {
            if (registry is null)
            {
                return;
            }

            IEnumerable<(string Resource, ActionDescriptor Action)> actions = registry.Stacks
                .SelectMany(static stack => stack.Actions.Select(action => (stack.Resource, action)))
                .Concat(registry.ChildQueries.SelectMany(static child => child.Actions.Select(action => (child.Resource, (ActionDescriptor)action))));
            foreach ((string resource, ActionDescriptor action) in actions)
            {
                keys.UnionWith(ActionKeys(resource, action.Name));

                // An argument field: the action's own key, the resource's member key, the shared key.
                if (ActionArguments.TypeOf(action) is Type arguments)
                {
                    foreach (ActionArguments.Member member in ActionArguments.Members(arguments))
                    {
                        keys.UnionWith(LabelChains.ActionField(resource, action.Name, LabelChains.FieldName(member.Property)));
                    }
                }
            }
        }

        /// <summary>The settings' labels, descriptions, groups and categories.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The setting-key registry; null when none is composed.</param>
        private static void AddSettings(HashSet<string> keys, ISettingKeyRegistry? registry)
        {
            _ = keys.Add($"SettingCategory_{GeneralCategory}");
            if (registry is null)
            {
                return;
            }

            foreach (SettingKey key in registry.Keys)
            {
                keys.UnionWith([key.LabelKey, key.LabelKey + "_Description"]);
                // The card's label: the stated group, else the one the key's name derives.
                if (SettingKeyRegistry.GroupOf(key) is { } group)
                {
                    _ = keys.Add($"SettingGroup_{SettingKeyRegistry.CategoryOf(key)}_{group.Replace('.', '_')}");
                }
            }

            keys.UnionWith(registry.Categories.Select(static category => category.LabelKey));
        }

        /// <summary>The notification types' texts and settings labels, the categories' labels, and the channels' labels.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="types">The notification type registry.</param>
        /// <param name="channels">The notification channel registry.</param>
        private static void AddNotifications(HashSet<string> keys, INotificationTypeRegistry types, INotificationChannelRegistry channels)
        {
            foreach (NotificationTypeDescriptor type in types.All)
            {
                string text = StringKeyConventions.FromDottedKey("Notification_", type.Key);
                keys.UnionWith([text, text + "_Detail", StringKeyConventions.FromDottedKey("NotificationType_", type.Key)]);
            }

            keys.UnionWith(types.Categories.Select(static category => "NotificationCategory_" + category));
            keys.UnionWith(channels.All.Select(static channel => StringKeyConventions.FromDottedKey("NotificationChannel_", channel.Key)));
        }

        /// <summary>The job handlers' labels.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The job handler registry; null where jobs are not composed.</param>
        private static void AddJobHandlers(HashSet<string> keys, JobHandlerRegistry? registry)
        {
            if (registry is not null)
            {
                keys.UnionWith(registry.Handlers.Select(static handler => JobHandlerKeys.LabelKeyOf(handler.Key)));
            }
        }

        /// <summary>The calendars' names and month names.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The calendar registry.</param>
        private static void AddCalendars(HashSet<string> keys, ICalendarRegistry registry)
        {
            foreach (string code in registry.Codes)
            {
                _ = keys.Add($"Calendar_{code}");
                keys.UnionWith(Enumerable.Range(1, registry[code].MonthCount).Select(month => $"Calendar_{code}_Month{month}"));
            }
        }

        /// <summary>The value sets' keyed labels.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The value-set registry.</param>
        private static void AddValueSets(HashSet<string> keys, IValueSetRegistry registry)
        {
            keys.UnionWith(registry.All.SelectMany(static set => set.Values).Select(static entry => entry.LabelKey).OfType<string>());
        }

        /// <summary>An action's keys on a resource: the resource's own, then the shared one.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action's name.</param>
        /// <returns>The two keys.</returns>
        private static string[] ActionKeys(string resource, string action)
        {
            return LabelChains.Action(resource, action);
        }
    }
}
