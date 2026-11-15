// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Localization
{
    // The chains are spec 0013 §7.6's, keyed as §10.1 spells keys; the setting chain is §5.4's.
    /// <summary>
    ///     Resolves labels by convention against the merged shared string resources of every composed
    ///     feature: each call walks its chain of keys, takes the first key the English (neutral)
    ///     resources declare, and renders that key in the reader's language, else in English; with no
    ///     key declared it renders the humanized name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reader's language is that of the flow's entered <see cref="LocalizationScope" />,
    ///         else of the current UI culture, as for every localizer. A multilingual group's columns
    ///         are suffixed with the symbols of the tenant's content languages, which the same scope,
    ///         else the request's localization, carries; outside a tenant there are none, and the
    ///         group reads as one column.
    ///     </para>
    ///     <para>
    ///         The key-resolving twins return the key the label resolves through, so the text a
    ///         client renders from its string pack and the text rendered here agree.
    ///     </para>
    /// </remarks>
    /// <param name="strings">The merged shared string resources.</param>
    /// <param name="metadata">The model's entity metadata.</param>
    /// <param name="valueSets">The composed value sets.</param>
    internal sealed class LabelProvider(StringPackComposer strings, IEntityMetadataProvider metadata, IValueSetRegistry valueSets) : ILabelProvider
    {
        /// <summary>The schemas of the entities that carry each enum, in model order; built on first use.</summary>
        private readonly Lazy<FrozenDictionary<Type, string[]>> _enumSchemas = new(
            () => EnumSchemas(metadata),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public string EntityLabel(Type entityType, bool plural = false)
        {
            EntityMetadata entity = Entity(entityType);
            return Render(EntityChain(entity, plural), EntityFallback(entity));
        }

        /// <inheritdoc />
        public string PropertyLabel(Type entityType, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(propertyName);
            EntityMetadata entity = Entity(entityType);

            // --- A property outside every multilingual group: its own chain ---
            if (Twin(entity, propertyName) is not (MultilingualGroup group, int position))
            {
                return Render(PropertyChain(entity, propertyName), NameHumanizer.Humanize(propertyName));
            }

            // --- A multilingual column: the primary's label, suffixed with its language's symbol ---
            // A mono-lingual tenant's primary is the group's one column and carries no suffix; a
            // column beyond the tenant's languages is offered to no one, so it reads as its name.
            IReadOnlyList<LanguageInfo> languages = ContentLanguages();
            if (position > Math.Max(1, languages.Count))
            {
                return NameHumanizer.Humanize(propertyName);
            }

            string primary = Render(PropertyChain(entity, group.Primary.Name), NameHumanizer.Humanize(group.Primary.Name));
            return languages.Count < 2 ? primary : $"{primary} ({languages[position - 1].Symbol})";
        }

        /// <inheritdoc />
        public string NavigationLabel(Type entityType, string navigation)
        {
            ArgumentNullException.ThrowIfNull(navigation);
            EntityMetadata entity = Entity(entityType);
            return Render(NavigationChain(entity, navigation), NameHumanizer.Humanize(navigation));
        }

        /// <inheritdoc />
        public string CollectionLabel(Type entityType, string collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            EntityMetadata entity = Entity(entityType);
            EntityMetadata? child = Child(entity, collection);
            return Render(CollectionChain(entity, collection, child), child is null ? NameHumanizer.Humanize(collection) : EntityFallback(child));
        }

        /// <inheritdoc />
        public string ActionLabel(Type entityType, string action)
        {
            ArgumentNullException.ThrowIfNull(action);
            EntityMetadata entity = Entity(entityType);
            // The humanized name reads the action's segment, so 'preferences/set' reads "Preferences set".
            return Render(ActionChain(entity, action), NameHumanizer.Humanize(StringKeyConventions.ActionSegment(action)));
        }

        /// <inheritdoc />
        public string SettingLabel(SettingKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Render([key.LabelKey], NameHumanizer.Humanize(StringKeyConventions.LastSegment(key.Name)));
        }

        /// <inheritdoc />
        public string EnumValueLabel(Type enumType, string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return Render(EnumChain(enumType, value), NameHumanizer.Humanize(value));
        }

        /// <inheritdoc />
        public string ValueLabel(string set, string value)
        {
            ArgumentNullException.ThrowIfNull(set);
            ArgumentNullException.ThrowIfNull(value);

            // A literal label reads the same in every language; a keyed one renders when declared;
            // a value the set lacks, or whose key no resource declares, reads as itself.
            return Entry(set, value) switch
            {
                { Label: { } label } => label,
                { LabelKey: { } key } when strings.Declares(key) => Text(key),
                _ => value,
            };
        }

        /// <inheritdoc />
        public string? EntityLabelKey(Type entityType, bool plural = false)
        {
            return FirstDeclared(EntityChain(Entity(entityType), plural));
        }

        /// <inheritdoc />
        public string? PropertyLabelKey(Type entityType, string propertyName)
        {
            ArgumentNullException.ThrowIfNull(propertyName);
            EntityMetadata entity = Entity(entityType);

            // A multilingual column resolves through its primary's key, whatever the tenant's languages.
            string resolved = Twin(entity, propertyName) is (MultilingualGroup group, _) ? group.Primary.Name : propertyName;
            return FirstDeclared(PropertyChain(entity, resolved));
        }

        /// <inheritdoc />
        public string? NavigationLabelKey(Type entityType, string navigation)
        {
            ArgumentNullException.ThrowIfNull(navigation);
            return FirstDeclared(NavigationChain(Entity(entityType), navigation));
        }

        /// <inheritdoc />
        public string? CollectionLabelKey(Type entityType, string collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            EntityMetadata entity = Entity(entityType);
            return FirstDeclared(CollectionChain(entity, collection, Child(entity, collection)));
        }

        /// <inheritdoc />
        public string? ActionLabelKey(Type entityType, string action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return FirstDeclared(ActionChain(Entity(entityType), action));
        }

        /// <inheritdoc />
        public string? SettingLabelKey(SettingKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return FirstDeclared([key.LabelKey]);
        }

        /// <inheritdoc />
        public string? EnumValueLabelKey(Type enumType, string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return FirstDeclared(EnumChain(enumType, value));
        }

        /// <inheritdoc />
        public string? ValueLabelKey(string set, string value)
        {
            ArgumentNullException.ThrowIfNull(set);
            ArgumentNullException.ThrowIfNull(value);

            // A literal label has no key; nor has a value the set lacks.
            return Entry(set, value) is { Label: null, LabelKey: { } key } && strings.Declares(key) ? key : null;
        }

        /// <summary>An entity's chain: <c>&lt;Prefix&gt;_Plural</c> for the plural, then <c>&lt;Prefix&gt;</c>.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="plural">Whether the plural label is wanted.</param>
        /// <returns>The keys, most specific first.</returns>
        private static string[] EntityChain(EntityMetadata entity, bool plural)
        {
            string prefix = StringKeyConventions.KeyPrefix(entity.Name);
            return plural ? [prefix + StringKeyConventions.PluralSuffix, prefix] : [prefix];
        }

        /// <summary>
        ///     A property's chain: the entity's key, then the shared key, which Core ships for the
        ///     members in <see cref="StringKeyConventions.SharedMemberLabels" />.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="property">The property's name.</param>
        /// <returns>The keys, most specific first.</returns>
        private static string[] PropertyChain(EntityMetadata entity, string property)
        {
            return [$"{StringKeyConventions.KeyPrefix(entity.Name)}_{property}", property];
        }

        /// <summary>A navigation's chain: the entity's key, its foreign key's chain when the model knows it, then the shared key.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="navigation">The navigation's name.</param>
        /// <returns>The keys, most specific first.</returns>
        private static string[] NavigationChain(EntityMetadata entity, string navigation)
        {
            ReferenceMetadata? reference = entity.References.FirstOrDefault(candidate => string.Equals(candidate.Navigation, navigation, StringComparison.Ordinal));
            string[] foreignKey = reference is null ? [] : PropertyChain(entity, reference.ForeignKey.Name);
            return [$"{StringKeyConventions.KeyPrefix(entity.Name)}_{navigation}", .. foreignKey, navigation];
        }

        /// <summary>A collection's chain: the entity's key, then the child entity's plural chain when the model knows it.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="collection">The collection's name.</param>
        /// <param name="child">The collection's child entity, or null when the model has no such collection.</param>
        /// <returns>The keys, most specific first.</returns>
        private static string[] CollectionChain(EntityMetadata entity, string collection, EntityMetadata? child)
        {
            string[] plural = child is null ? [] : EntityChain(child, plural: true);
            return [$"{StringKeyConventions.KeyPrefix(entity.Name)}_{collection}", .. plural];
        }

        /// <summary>An action's chain: the entity's key, then the shared <c>Action_</c> key.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="action">The action's name.</param>
        /// <returns>The keys, most specific first.</returns>
        private static string[] ActionChain(EntityMetadata entity, string action)
        {
            string segment = StringKeyConventions.ActionSegment(action);
            return [StringKeyConventions.KeyPrefix(entity.Name) + StringKeyConventions.ActionInfix + segment, StringKeyConventions.ActionPrefix + segment];
        }

        /// <summary>An entity's last resort: its humanized name, the last segment of its qualified name.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The label.</returns>
        private static string EntityFallback(EntityMetadata entity)
        {
            return NameHumanizer.Humanize(StringKeyConventions.LastSegment(entity.Name));
        }

        /// <summary>The multilingual group a property is a column of, and its position in it.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="property">The property's name.</param>
        /// <returns>The group and the position (1 to 3), or null for a property of no group.</returns>
        private static (MultilingualGroup Group, int Position)? Twin(EntityMetadata entity, string property)
        {
            foreach (MultilingualGroup group in entity.MultilingualGroups)
            {
                if (string.Equals(group.Primary.Name, property, StringComparison.Ordinal))
                {
                    return (group, 1);
                }

                if (string.Equals(group.Secondary?.Name, property, StringComparison.Ordinal))
                {
                    return (group, 2);
                }

                if (string.Equals(group.Tertiary?.Name, property, StringComparison.Ordinal))
                {
                    return (group, 3);
                }
            }

            return null;
        }

        /// <summary>The child entity of a collection.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="collection">The collection's name.</param>
        /// <returns>The child entity, or null when the entity has no such collection.</returns>
        private static EntityMetadata? Child(EntityMetadata entity, string collection)
        {
            return entity.Children.FirstOrDefault(candidate => string.Equals(candidate.Property, collection, StringComparison.Ordinal))?.Child;
        }

        /// <summary>The schemas of the entities that carry each enum, in model order.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <returns>The schemas' key spellings by enum type.</returns>
        private static FrozenDictionary<Type, string[]> EnumSchemas(IEntityMetadataProvider metadata)
        {
            Dictionary<Type, List<string>> schemas = [];
            foreach (EntityMetadata entity in metadata.All)
            {
                foreach (PropertyMetadata property in entity.Properties)
                {
                    Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (!type.IsEnum)
                    {
                        continue;
                    }

                    string schema = StringKeyConventions.SchemaPrefix(entity.Name);
                    if (!schemas.TryGetValue(type, out List<string>? list))
                    {
                        list = [];
                        schemas[type] = list;
                    }

                    if (!list.Contains(schema, StringComparer.Ordinal))
                    {
                        list.Add(schema);
                    }
                }
            }

            return schemas.ToFrozenDictionary(static entry => entry.Key, static entry => entry.Value.ToArray());
        }

        /// <summary>The tenant's content languages on this flow: the entered scope's, else the request's; none outside a tenant.</summary>
        /// <returns>The languages, in position order.</returns>
        private static IReadOnlyList<LanguageInfo> ContentLanguages()
        {
            return (LocalizationScope.Current ?? LocalizationScope.Request)?.ContentLanguages ?? [];
        }

        /// <summary>The reader's language: the bare language subtag of the flow's culture, English for the invariant culture.</summary>
        /// <returns>The language code.</returns>
        private static string ReaderLanguage()
        {
            CultureInfo culture = LocalizationScope.Current?.Culture ?? CultureInfo.CurrentUICulture;
            string name = culture.Name;
            if (name.Length == 0)
            {
                return LanguageSet.English;
            }

            int dash = name.IndexOf('-', StringComparison.Ordinal);
            return dash < 0 ? name : name[..dash];
        }

        /// <summary>An entity's metadata.</summary>
        /// <param name="entityType">The entity's type.</param>
        /// <returns>The metadata.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entityType" /> is null.</exception>
        /// <exception cref="ArgumentException">The model has no such entity.</exception>
        private EntityMetadata Entity(Type entityType)
        {
            ArgumentNullException.ThrowIfNull(entityType);
            return metadata.Get(entityType);
        }

        /// <summary>An enum member's chain: one key per schema of the entities that carry the enum, in model order.</summary>
        /// <param name="enumType">The enum, or a nullable of it.</param>
        /// <param name="value">The member's name.</param>
        /// <returns>The keys; none when no entity carries the enum.</returns>
        /// <exception cref="ArgumentException"><paramref name="enumType" /> is not an enum.</exception>
        private string[] EnumChain(Type enumType, string value)
        {
            ArgumentNullException.ThrowIfNull(enumType);
            Type type = Nullable.GetUnderlyingType(enumType) ?? enumType;
            return !type.IsEnum
                ? throw new ArgumentException($"An enum value's label needs an enum type; received '{enumType}'.", nameof(enumType))
                : _enumSchemas.Value.TryGetValue(type, out string[]? schemas)
                    ? [.. schemas.Select(schema => $"{schema}_{type.Name}_{value}")]
                    : [];
        }

        /// <summary>A value set's entry for a value, compared without regard to case as the set's values are unique.</summary>
        /// <param name="set">The set's name.</param>
        /// <param name="value">The value.</param>
        /// <returns>The entry, or null when there is no such set or value.</returns>
        private ValueSetEntry? Entry(string set, string value)
        {
            return valueSets.Find(set)?.Values.FirstOrDefault(entry => string.Equals(entry.Value, value, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The first key of a chain the English resources declare.</summary>
        /// <param name="chain">The keys, most specific first.</param>
        /// <returns>The key, or null when none is declared.</returns>
        private string? FirstDeclared(IEnumerable<string> chain)
        {
            return chain.FirstOrDefault(strings.Declares);
        }

        /// <summary>A chain's label: its first declared key's text, else the fallback.</summary>
        /// <param name="chain">The keys, most specific first.</param>
        /// <param name="fallback">The label when no key is declared.</param>
        /// <returns>The label.</returns>
        private string Render(IEnumerable<string> chain, string fallback)
        {
            return FirstDeclared(chain) is { } key ? Text(key) : fallback;
        }

        /// <summary>A declared key's text in the reader's language, else in English.</summary>
        /// <param name="key">The key, which the English resources declare.</param>
        /// <returns>The text.</returns>
        private string Text(string key)
        {
            return strings.Strings(ReaderLanguage()) is { } dictionary && dictionary.TryGetValue(key, out string? text)
                ? text
                : strings.English[key];
        }
    }
}
