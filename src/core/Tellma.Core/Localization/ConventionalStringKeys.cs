// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Access;
using Tellma.Core.Jobs;

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
    ///             each property's, navigation's and child collection's <c>…_&lt;Member&gt;</c> and
    ///             the shared <c>&lt;Member&gt;</c> (a multilingual group's twins have none, as they
    ///             take their primary's label); each enum member's
    ///             <c>&lt;Schema&gt;_&lt;Enum&gt;_&lt;Value&gt;</c>; and <c>Entity_New</c>;
    ///         </item>
    ///         <item>
    ///             each securable's <c>Securable_&lt;resource&gt;</c>, its action's
    ///             <c>&lt;Resource&gt;_Action_&lt;Name&gt;</c> and <c>Action_&lt;Name&gt;</c>, its
    ///             feature's <c>Feature_&lt;feature&gt;</c>, and a settings resource's
    ///             <c>SettingCategory_&lt;category&gt;</c>; and <c>Action_&lt;Name&gt;</c> of each of
    ///             the platform's own actions, which any resource may register;
    ///         </item>
    ///         <item>
    ///             with stacks composed, each stack action's keys and those of its argument fields,
    ///             <c>&lt;Resource&gt;_Action_&lt;Name&gt;_&lt;Field&gt;</c>,
    ///             <c>&lt;Resource&gt;_&lt;Field&gt;</c> and <c>&lt;Field&gt;</c>;
    ///         </item>
    ///         <item>
    ///             with setting keys composed, each key's label and <c>…_Description</c>, its group's
    ///             <c>SettingGroup_&lt;category&gt;_&lt;group&gt;</c> and each category's label;
    ///             <c>SettingCategory_general</c> always;
    ///         </item>
    ///         <item>
    ///             each notification type's <c>Notification_&lt;key&gt;</c>, <c>…_Detail</c> and
    ///             <c>NotificationType_&lt;key&gt;</c>; each channel's
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
            AddEntities(keys, services.GetRequiredService<IEntityMetadataProvider>());
            AddSecurables(keys, services.GetRequiredService<ISecurableRegistry>());
            AddStacks(keys, services.GetService<IStackRegistry>());
            AddSettings(keys, services.GetService<ISettingKeyRegistry>());
            AddNotifications(keys, services.GetRequiredService<INotificationTypeRegistry>(), services.GetRequiredService<INotificationChannelRegistry>());
            AddJobHandlers(keys, services.GetService<JobHandlerRegistry>());
            AddCalendars(keys, services.GetRequiredService<ICalendarRegistry>());
            AddValueSets(keys, services.GetRequiredService<IValueSetRegistry>());
            keys.UnionWith(DistributionKeys);
            return keys;
        }

        /// <summary>The entities' labels, their members' labels with the shared fallbacks, and their enums' values.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        private static void AddEntities(HashSet<string> keys, IEntityMetadataProvider metadata)
        {
            _ = keys.Add(StringKeyConventions.EntityNew);
            foreach (EntityMetadata entity in metadata.All)
            {
                // --- The entity: singular, plural, create ---
                string prefix = StringKeyConventions.KeyPrefix(entity.Name);
                keys.UnionWith([prefix, prefix + StringKeyConventions.PluralSuffix, prefix + StringKeyConventions.NewSuffix]);

                // --- Its members: each with its own key and the shared fallback ---
                // A multilingual group's twins take the primary's label, so they have no key of their own.
                HashSet<string> twins = [.. entity.MultilingualGroups
                    .SelectMany(static group => new[] { group.Secondary?.Name, group.Tertiary?.Name })
                    .OfType<string>()];

                IEnumerable<string> members = entity.Properties.Select(static property => property.Name).Where(name => !twins.Contains(name))
                    .Concat(entity.References.Select(static reference => reference.Navigation))
                    .Concat(entity.Children.Select(static child => child.Property));
                foreach (string member in members)
                {
                    keys.UnionWith([$"{prefix}_{member}", member]);
                }

                // --- Its enums' values, under the entity's schema ---
                string schema = StringKeyConventions.SchemaPrefix(entity.Name);
                foreach (PropertyMetadata property in entity.Properties)
                {
                    Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (type.IsEnum)
                    {
                        keys.UnionWith(Enum.GetNames(type).Select(value => $"{schema}_{type.Name}_{value}"));
                    }
                }
            }
        }

        /// <summary>The securables' resource, action and feature labels.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The securable registry.</param>
        private static void AddSecurables(HashSet<string> keys, ISecurableRegistry registry)
        {
            // The platform's actions share Core's labels, whichever resources register them.
            keys.UnionWith(SecurableRegistry.PlatformActionOrder.Select(static action => StringKeyConventions.ActionPrefix + StringKeyConventions.ActionSegment(action)));
            foreach (SecurableDescriptor securable in registry.All)
            {
                keys.UnionWith(
                [
                    StringKeyConventions.FromDottedKey("Securable_", securable.Resource),
                    StringKeyConventions.FromDottedKey("Feature_", securable.Feature),
                    .. ActionKeys(securable.Resource, securable.Action),
                ]);

                // A settings resource is labelled as its category's tab.
                if (securable.Resource.StartsWith(CoreResources.SettingsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string category = string.Equals(securable.Resource, CoreResources.SettingsGeneral, StringComparison.OrdinalIgnoreCase)
                        ? GeneralCategory
                        : securable.Resource[CoreResources.SettingsPrefix.Length..];
                    _ = keys.Add($"SettingCategory_{category}");
                }
            }
        }

        /// <summary>The stacks' and routed services' action labels and their argument fields' labels.</summary>
        /// <param name="keys">The keys so far.</param>
        /// <param name="registry">The stack registry; null when no stack is composed.</param>
        private static void AddStacks(HashSet<string> keys, IStackRegistry? registry)
        {
            if (registry is null)
            {
                return;
            }

            foreach (StackDescriptor stack in registry.Stacks)
            {
                foreach (ActionDescriptor action in stack.Actions)
                {
                    keys.UnionWith(ActionKeys(stack.Resource, action.Name));
                    Type? arguments = action switch
                    {
                        EntityActionDescriptor entityAction => entityAction.ArgumentsType,
                        ApiActionDescriptor apiAction => apiAction.RequestType,
                        _ => null,
                    };

                    // An argument field: the action's own key, the resource's member key, the shared key.
                    string prefix = StringKeyConventions.KeyPrefix(stack.Resource);
                    foreach (PropertyInfo field in arguments?.GetProperties(BindingFlags.Public | BindingFlags.Instance) ?? [])
                    {
                        keys.UnionWith(
                        [
                            $"{prefix}{StringKeyConventions.ActionInfix}{StringKeyConventions.ActionSegment(action.Name)}_{field.Name}",
                            $"{prefix}_{field.Name}",
                            field.Name,
                        ]);
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
                if (key.Group is { } group)
                {
                    _ = keys.Add($"SettingGroup_{key.Category}_{group.Replace('.', '_')}");
                }
            }

            keys.UnionWith(registry.Categories.Select(static category => category.LabelKey));
        }

        /// <summary>The notification types' texts and settings labels, and the channels' labels.</summary>
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
            string segment = StringKeyConventions.ActionSegment(action);
            return [StringKeyConventions.KeyPrefix(resource) + StringKeyConventions.ActionInfix + segment, StringKeyConventions.ActionPrefix + segment];
        }
    }
}
