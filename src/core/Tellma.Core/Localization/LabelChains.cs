// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Localization
{
    // The chains are spec 0013 §7.6's, keyed as §10.1 spells keys, plus the two the client metadata
    // adds (spec 0029 §2.4): the create label and an action's argument field. The label provider
    // renders through them on the server and the client metadata resolves its label keys through
    // them, so both name a label by the same key.
    /// <summary>
    ///     The key chains of the platform's labels: for each label, the resource keys a lookup tries,
    ///     most specific first, and the humanized English that stands in when no key is declared.
    /// </summary>
    /// <remarks>
    ///     Every chain is a pure function of names, so a chain reads the same for an entity with
    ///     metadata and for a read-only root that has none.
    /// </remarks>
    internal static class LabelChains
    {
        /// <summary>The suffix an <see cref="EntityRefAttribute" /> argument's name drops in its label's key.</summary>
        private const string IdSuffix = "Id";

        /// <summary>An entity's singular label: <c>&lt;Schema&gt;_&lt;Entity&gt;</c>.</summary>
        /// <param name="entity">The entity's qualified name.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] Entity(string entity)
        {
            return [StringKeyConventions.KeyPrefix(entity)];
        }

        /// <summary>An entity's plural label: <c>&lt;Schema&gt;_&lt;Entity&gt;_Plural</c>, then the singular label.</summary>
        /// <param name="entity">The entity's qualified name.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] Plural(string entity)
        {
            string prefix = StringKeyConventions.KeyPrefix(entity);
            return [prefix + StringKeyConventions.PluralSuffix, prefix];
        }

        /// <summary>An entity's create label: <c>&lt;Schema&gt;_&lt;Entity&gt;_New</c> ("New customer"), then Core's <c>Entity_New</c> ("New").</summary>
        /// <param name="entity">The entity's qualified name.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] New(string entity)
        {
            return [StringKeyConventions.KeyPrefix(entity) + StringKeyConventions.NewSuffix, StringKeyConventions.EntityNew];
        }

        /// <summary>
        ///     A property's label: the entity's key, then the shared key, which Core ships for the members
        ///     in <see cref="StringKeyConventions.SharedMemberLabels" />.
        /// </summary>
        /// <param name="entity">The entity's qualified name.</param>
        /// <param name="property">The property's name; a multilingual twin passes its anchor's.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] Property(string entity, string property)
        {
            return [$"{StringKeyConventions.KeyPrefix(entity)}_{property}", property];
        }

        /// <summary>A navigation's label: the entity's key, its foreign key's property chain when it has one, then the shared key.</summary>
        /// <param name="entity">The entity's qualified name.</param>
        /// <param name="navigation">The navigation's name.</param>
        /// <param name="foreignKey">The foreign key's property name; null for a navigation that holds none of its own, such as a sibling's.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] Navigation(string entity, string navigation, string? foreignKey)
        {
            string[] key = foreignKey is null ? [] : Property(entity, foreignKey);
            return [$"{StringKeyConventions.KeyPrefix(entity)}_{navigation}", .. key, navigation];
        }

        /// <summary>A collection's label: the entity's key, then the child entity's plural chain when the child is known.</summary>
        /// <param name="entity">The entity's qualified name.</param>
        /// <param name="collection">The collection's name.</param>
        /// <param name="child">The child entity's qualified name; null when the model has no such collection.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] Collection(string entity, string collection, string? child)
        {
            string[] plural = child is null ? [] : Plural(child);
            return [$"{StringKeyConventions.KeyPrefix(entity)}_{collection}", .. plural];
        }

        /// <summary>An action's label: the entity's key, then the shared <c>Action_</c> key.</summary>
        /// <param name="entity">The entity's qualified name, or the resource the action belongs to.</param>
        /// <param name="action">The action's name.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] Action(string entity, string action)
        {
            string segment = StringKeyConventions.ActionSegment(action);
            return [StringKeyConventions.KeyPrefix(entity) + StringKeyConventions.ActionInfix + segment, StringKeyConventions.ActionPrefix + segment];
        }

        /// <summary>
        ///     An action's argument field: the action's own key, then the entity's member key, then the
        ///     shared key. The field is named by <see cref="FieldName" />.
        /// </summary>
        /// <param name="entity">The entity's qualified name, or the resource the action belongs to.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="field">The field's name in keys.</param>
        /// <returns>The keys, most specific first.</returns>
        public static string[] ActionField(string entity, string action, string field)
        {
            string prefix = StringKeyConventions.KeyPrefix(entity);
            return [$"{prefix}{StringKeyConventions.ActionInfix}{StringKeyConventions.ActionSegment(action)}_{field}", $"{prefix}_{field}", field];
        }

        /// <summary>An enum value's label: one key per schema that carries the enum, in the order the schemas carry it.</summary>
        /// <param name="keyPrefixes">The schemas' key spellings, such as <c>Gl</c>.</param>
        /// <param name="enumName">The enum type's name, such as <c>CenterType</c>.</param>
        /// <param name="value">The member's name.</param>
        /// <returns>The keys, most specific first; none when no schema carries the enum.</returns>
        public static string[] EnumValue(IReadOnlyList<string> keyPrefixes, string enumName, string value)
        {
            return [.. keyPrefixes.Select(prefix => $"{prefix}_{enumName}_{value}")];
        }

        /// <summary>
        ///     The name an action's argument field takes in its label keys: the C# property's name, less
        ///     the trailing <c>Id</c> of an <see cref="EntityRefAttribute" /> property, so <c>AssigneeId</c>
        ///     reads <c>Assignee</c>.
        /// </summary>
        /// <param name="property">The argument's property.</param>
        /// <returns>The name.</returns>
        public static string FieldName(PropertyInfo property)
        {
            ArgumentNullException.ThrowIfNull(property);
            string name = property.Name;
            return property.GetCustomAttribute<EntityRefAttribute>(inherit: true) is not null
                && name.Length > IdSuffix.Length
                && name.EndsWith(IdSuffix, StringComparison.Ordinal)
                ? name[..^IdSuffix.Length]
                : name;
        }

        /// <summary>An entity's last resort: its humanized name, the last segment of its qualified name.</summary>
        /// <param name="entity">The entity's qualified name.</param>
        /// <returns>The label.</returns>
        public static string EntityFallback(string entity)
        {
            return NameHumanizer.Humanize(StringKeyConventions.LastSegment(entity));
        }

        /// <summary>An action's last resort: its humanized segment, so <c>preferences/set</c> reads "Preferences set".</summary>
        /// <param name="action">The action's name.</param>
        /// <returns>The label.</returns>
        public static string ActionFallback(string action)
        {
            return NameHumanizer.Humanize(StringKeyConventions.ActionSegment(action));
        }

        /// <summary>The first key of a chain the neutral resources declare.</summary>
        /// <param name="strings">The keys the neutral resources declare.</param>
        /// <param name="chain">The keys, most specific first.</param>
        /// <returns>The key, or null when none is declared.</returns>
        public static string? FirstDeclared(INeutralStringKeys strings, IEnumerable<string> chain)
        {
            ArgumentNullException.ThrowIfNull(strings);
            ArgumentNullException.ThrowIfNull(chain);
            return chain.FirstOrDefault(strings.Declares);
        }

        /// <summary>
        ///     A chain's key as the client metadata carries it: the first key a neutral resource declares,
        ///     else the chain's first, conventional key with the fallback a client shows in every language.
        /// </summary>
        /// <param name="strings">The keys the neutral resources declare.</param>
        /// <param name="chain">The keys, most specific first; at least one.</param>
        /// <param name="fallback">The label when no key is declared.</param>
        /// <returns>The key, and the fallback when no key is declared.</returns>
        /// <exception cref="ArgumentException"><paramref name="chain" /> is empty.</exception>
        public static (string Key, string? Fallback) Resolve(INeutralStringKeys strings, IReadOnlyList<string> chain, string fallback)
        {
            ArgumentNullException.ThrowIfNull(chain);
            return chain.Count == 0
                ? throw new ArgumentException("A label's key chain names at least one key.", nameof(chain))
                : FirstDeclared(strings, chain) is { } key ? (key, null) : (chain[0], fallback);
        }
    }
}
