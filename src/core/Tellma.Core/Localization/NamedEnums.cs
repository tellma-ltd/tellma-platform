// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Client;
using Tellma.Core.Settings;

namespace Tellma.Core.Localization
{
    // Spec 0029 §2.2 and spec 0013 §7.7: a C# enum and a value set are one kind, a named enum, written
    // once and named by every property, setting key and action field that takes its values. A C# enum
    // is named by a schema and its type's name (gl.CenterType), the namespace value sets live in; the
    // schema is the declaring feature's, so the name does not move with the order features compose in.
    /// <summary>
    ///     One C# enum of the composition: its name on every surface, and the schemas whose keys label
    ///     its values.
    /// </summary>
    /// <param name="Type">The enum type.</param>
    /// <param name="Name">The enum's name, <c>&lt;schema&gt;.&lt;TypeName&gt;</c> by its home schema.</param>
    /// <param name="KeyPrefixes">The key spellings of every schema that carries it, such as <c>Gl</c>: its home schema's first, then the others in ordinal order.</param>
    internal sealed record NamedEnum(Type Type, string Name, IReadOnlyList<string> KeyPrefixes)
    {
        /// <summary>The enum's members in value order: each member's name, which its label keys use, and its wire name.</summary>
        /// <returns>The members.</returns>
        public IReadOnlyList<(string Name, string Wire)> Members()
        {
            return [.. Enum.GetNames(Type).Select(name => (name, NamedEnums.WireName(Type, name)))];
        }
    }

    /// <summary>
    ///     The C# enums the composition names: those of the entities' properties in model order, then
    ///     those of the setting keys in registry order, then those of the stacks' action arguments in
    ///     stack order. Each enum is named once, by its home schema, and its values are labelled by the
    ///     keys of every schema that carries it. The home schema is the ordinally smallest schema among
    ///     the entities, and the action arguments of stacks over entities, declared in the enum's own
    ///     assembly that carry it, else among everything that carries it, so neither the order features
    ///     compose in nor the stacks another feature adds ever changes it. The platform registers it as
    ///     a singleton, built on first use.
    /// </summary>
    /// <param name="metadata">The model's entity metadata.</param>
    /// <param name="settings">The setting keys; null in a composition without them.</param>
    /// <param name="stacks">The stacks; null in a composition without them.</param>
    internal sealed class NamedEnums(IEntityMetadataProvider metadata, ISettingKeyRegistry? settings, IStackRegistry? stacks)
    {
        /// <summary>The enums, built on first use.</summary>
        private readonly Lazy<Contents> _contents = new(
            () => Contents.Of(Discover(metadata, settings?.Keys ?? [], stacks)),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Every C# enum the composition names, in discovery order.</summary>
        public IReadOnlyList<NamedEnum> All => _contents.Value.All;

        /// <summary>An enum by its type.</summary>
        /// <param name="enumType">The enum type, or a nullable of it.</param>
        /// <returns>The enum, or null when nothing in the composition carries it.</returns>
        public NamedEnum? Find(Type enumType)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            return _contents.Value.ByType.GetValueOrDefault(Nullable.GetUnderlyingType(enumType) ?? enumType);
        }

        /// <summary>
        ///     Discovers the C# enums of a composition. A source that does not build is left out by its
        ///     caller, so a startup check can name what the rest carries.
        /// </summary>
        /// <param name="metadata">The model's entity metadata; null when it does not build.</param>
        /// <param name="settingKeys">The setting keys, in registry order.</param>
        /// <param name="stacks">The stacks; null when they do not build.</param>
        /// <returns>The enums, in discovery order.</returns>
        public static IReadOnlyList<NamedEnum> Discover(IEntityMetadataProvider? metadata, IEnumerable<SettingKey> settingKeys, IStackRegistry? stacks)
        {
            ArgumentNullException.ThrowIfNull(settingKeys);

            // --- The sites that carry an enum, in order: entity properties, setting keys, action arguments ---
            // Each site notes the assembly that declares it, when it has one: an entity's class, or an
            // action's arguments type when the stack's entity is declared beside it. A setting key's
            // declaring type is not known here, so it has none.
            List<(string Owner, Type Type, Assembly? Declarer)> sites = [];
            if (metadata is not null)
            {
                foreach (EntityMetadata entity in metadata.All)
                {
                    sites.AddRange(entity.Properties
                        .Select(static property => Unwrap(property.ClrType))
                        .OfType<Type>()
                        .Select(type => (entity.Name, type, (Assembly?)entity.ClrType.Assembly)));
                }
            }

            foreach (SettingKey key in settingKeys)
            {
                if (!key.HasOwnContract && SettingValueShape.TryRead(key.ValueType, out SettingValueShape shape) && shape.ScalarType.IsEnum)
                {
                    sites.Add((key.Name, shape.ScalarType, null));
                }
            }

            if (stacks is not null)
            {
                sites.AddRange(ActionSites(stacks));
            }

            // --- One enum per type, in discovery order: named by its home schema, labelled by every site's ---
            List<NamedEnum> found = [];
            foreach (IGrouping<Type, (string Owner, Type Type, Assembly? Declarer)> carried in sites.GroupBy(static site => site.Type))
            {
                Type type = carried.Key;
                string[] home = [.. carried.Where(site => site.Declarer == type.Assembly).Select(static site => site.Owner)];
                string owner = (home.Length > 0 ? home : carried.Select(static site => site.Owner))
                    .OrderBy(SchemaOf, StringComparer.Ordinal)
                    .First();
                string first = StringKeyConventions.SchemaPrefix(owner);
                IEnumerable<string> others = carried
                    .Select(static site => StringKeyConventions.SchemaPrefix(site.Owner))
                    .Where(prefix => !string.Equals(prefix, first, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal);
                found.Add(new NamedEnum(type, NameOf(owner, type), [first, .. others]));
            }

            return found.AsReadOnly();
        }

        /// <summary>The schema of a qualified name: the text before its first dot.</summary>
        /// <param name="owner">The qualified name, such as <c>gl.Center</c>.</param>
        /// <returns>The schema, such as <c>gl</c>.</returns>
        private static string SchemaOf(string owner)
        {
            int dot = owner.IndexOf('.', StringComparison.Ordinal);
            return dot < 0 ? owner : owner[..dot];
        }

        /// <summary>The name of an enum a site carries: the site's schema, a dot and the type's name.</summary>
        /// <param name="owner">The qualified name of the entity, setting key or resource that carries it, such as <c>gl.Center</c>.</param>
        /// <param name="type">The enum type.</param>
        /// <returns>The name, such as <c>gl.CenterType</c>.</returns>
        public static string NameOf(string owner, Type type)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(type);
            return $"{SchemaOf(owner)}.{type.Name}";
        }

        /// <summary>
        ///     The enum a value of a type takes: the type itself, its nullable form, or the element of a
        ///     list of either.
        /// </summary>
        /// <param name="type">The value's type.</param>
        /// <returns>The enum type, or null when the value takes none.</returns>
        public static Type? Unwrap(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            Type scalar = ClientDataTypes.ElementOf(type) ?? type;
            scalar = Nullable.GetUnderlyingType(scalar) ?? scalar;
            return scalar.IsEnum ? scalar : null;
        }

        /// <summary>A member's wire name: the name its <see cref="JsonStringEnumMemberNameAttribute" /> declares, else its own.</summary>
        /// <param name="enumType">The enum type.</param>
        /// <param name="member">The member's name.</param>
        /// <returns>The wire name.</returns>
        public static string WireName(Type enumType, string member)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            ArgumentNullException.ThrowIfNull(member);
            return enumType.GetField(member, BindingFlags.Public | BindingFlags.Static)?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? member;
        }

        /// <summary>
        ///     The enums the stacks' action arguments carry, each under the resource it belongs to and
        ///     with the assembly that declares the arguments type, when that assembly declares the
        ///     stack's entity too: a stack's actions, then its child query surfaces' actions.
        /// </summary>
        /// <param name="stacks">The stacks.</param>
        /// <returns>The sites, in stack order.</returns>
        private static IEnumerable<(string Owner, Type Type, Assembly? Declarer)> ActionSites(IStackRegistry stacks)
        {
            IEnumerable<(string Resource, Type Entity, ActionDescriptor Action)> actions = stacks.Stacks
                .SelectMany(static stack => stack.Actions.Select(action => (stack.Resource, stack.EntityType, action)))
                .Concat(stacks.ChildQueries.SelectMany(static child => child.Actions.Select(action => (child.Resource, child.EntityType, (ActionDescriptor)action))));
            foreach ((string resource, Type entity, ActionDescriptor action) in actions)
            {
                if (ActionArguments.TypeOf(action) is not Type arguments)
                {
                    continue;
                }

                // A platform action rides every stack, another feature's too: its arguments' assembly
                // declares the site only on a stack over that assembly's own entity, so such a stack
                // never names the enum.
                Assembly? declarer = entity.Assembly == arguments.Assembly ? arguments.Assembly : null;
                foreach (ActionArguments.Member member in ActionArguments.Members(arguments))
                {
                    if (Unwrap(member.Json.PropertyType) is Type type)
                    {
                        yield return (resource, type, declarer);
                    }
                }
            }
        }

        /// <summary>The discovered enums, in order and by type.</summary>
        /// <param name="All">Every enum, in discovery order.</param>
        /// <param name="ByType">Every enum, by its type.</param>
        private sealed record Contents(IReadOnlyList<NamedEnum> All, FrozenDictionary<Type, NamedEnum> ByType)
        {
            /// <summary>Indexes discovered enums.</summary>
            /// <param name="all">The enums.</param>
            /// <returns>The contents.</returns>
            public static Contents Of(IReadOnlyList<NamedEnum> all)
            {
                return new Contents(all, all.ToFrozenDictionary(static item => item.Type));
            }
        }
    }
}
