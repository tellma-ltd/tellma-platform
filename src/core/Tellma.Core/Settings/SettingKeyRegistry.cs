// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Composition;
using Tellma.Core.Localization;

namespace Tellma.Core.Settings
{
    // The registry is spec 0013 §5.3: keys discovered from the contributed holder types, the
    // categories in tab order, and the descriptors the client metadata carries (§1.2).
    /// <summary>
    ///     Every setting key of the composition, discovered from the public static
    ///     <see cref="SettingKey{T}" /> fields of each contributed type, with the settings tabs they
    ///     form and the descriptors the settings editor renders them from.
    /// </summary>
    /// <remarks>
    ///     The registry trusts the startup gate: it lists every key it finds, and the gate refuses a
    ///     composition whose keys break a declaration rule. Of two keys with one name, the first is
    ///     the one <see cref="Find" /> returns.
    /// </remarks>
    internal sealed class SettingKeyRegistry : ISettingKeyRegistry
    {
        /// <summary>The category the tenant's typed settings share, compared ordinally without regard to case.</summary>
        public const string GeneralCategory = "general";

        /// <summary>The prefix of a category's label key.</summary>
        public const string CategoryLabelPrefix = "SettingCategory_";

        /// <summary>The suffix of a key's description key, after its label key.</summary>
        public const string DescriptionSuffix = "_Description";

        /// <summary>The JSON null a secret key's descriptor carries for its default.</summary>
        private static readonly JsonElement JsonNull = JsonDocument.Parse("null").RootElement.Clone();

        /// <summary>The declarations, a field that holds no key left out.</summary>
        private readonly ImmutableArray<DeclaredSettingKey> _declared;

        /// <summary>The keys by name; the first of two with one name.</summary>
        private readonly FrozenDictionary<string, SettingKey> _byName;

        /// <summary>The resource keys the composition's neutral strings declare.</summary>
        private readonly INeutralStringKeys _strings;

        /// <summary>The entities, which name what a key references.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The descriptors, built on first use: the string pack and the model are read then.</summary>
        private readonly Lazy<IReadOnlyList<SettingKeyDescriptor>> _descriptors;

        /// <summary>Creates the registry over a composition's contributed setting-key types.</summary>
        /// <param name="record">The composition's record.</param>
        /// <param name="strings">The resource keys the composition's neutral strings declare.</param>
        /// <param name="metadata">The entities, which name what a key references.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SettingKeyRegistry(TellmaCompositionRecord record, INeutralStringKeys strings, IEntityMetadataProvider metadata)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(strings);
            ArgumentNullException.ThrowIfNull(metadata);
            _strings = strings;
            _metadata = metadata;

            // --- The keys, in contribution and declaration order ---
            _declared = [.. DeclaredSettingKey.Discover(record.SettingKeyTypes).Where(static declared => declared.Key is not null)];
            Keys = [.. _declared.Select(static declared => declared.Key!)];
            Dictionary<string, SettingKey> byName = new(StringComparer.Ordinal);
            foreach (SettingKey key in Keys)
            {
                _ = byName.TryAdd(key.Name, key);
            }

            _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);

            // --- The tabs: general first, then each category as its first key appears ---
            Categories = BuildCategories(Keys);
            _descriptors = new Lazy<IReadOnlyList<SettingKeyDescriptor>>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public IReadOnlyList<SettingKey> Keys { get; }

        /// <inheritdoc />
        public IReadOnlyList<SettingCategoryDescriptor> Categories { get; }

        /// <summary>
        ///     A key's category as the tabs name it: <see cref="GeneralCategory" /> for any spelling of
        ///     it, else the key's category verbatim.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The category.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public static string CategoryOf(SettingKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return IsGeneral(key.Category) ? GeneralCategory : key.Category;
        }

        /// <summary>
        ///     A key's group, the card inside its category: its stated group, else the one its name
        ///     derives (<see cref="SettingKey.DeriveGroup" />); null for none.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The group, or null.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public static string? GroupOf(SettingKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return key.Group ?? SettingKey.DeriveGroup(key.Name, key.Category);
        }

        /// <summary>Whether a category is the general one, compared ordinally without regard to case.</summary>
        /// <param name="category">The category.</param>
        /// <returns>True for any spelling of <see cref="GeneralCategory" />.</returns>
        public static bool IsGeneral(string? category)
        {
            return string.Equals(category, GeneralCategory, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public SettingKey? Find(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return _byName.GetValueOrDefault(name);
        }

        /// <inheritdoc />
        public IReadOnlyList<SettingKeyDescriptor> Describe()
        {
            return _descriptors.Value;
        }

        /// <summary>The tabs: general always, first, then each other category in the order its first key appears.</summary>
        /// <param name="keys">The keys, in registry order.</param>
        /// <returns>The categories, their order their positions.</returns>
        private static ImmutableArray<SettingCategoryDescriptor> BuildCategories(IReadOnlyList<SettingKey> keys)
        {
            // Securables compare without regard to case, so two spellings of one category are one tab
            // (the gate refuses them); the first spelling names it.
            List<string> names = [GeneralCategory];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { GeneralCategory };
            foreach (SettingKey key in keys)
            {
                if (seen.Add(key.Category))
                {
                    names.Add(key.Category);
                }
            }

            return [.. names.Select(static (name, index) => new SettingCategoryDescriptor(
                name,
                IsGeneral(name) ? CoreResources.SettingsGeneral : CoreResources.SettingsPrefix + name,
                index,
                CategoryLabelPrefix + name))];
        }

        /// <summary>One descriptor per key, in registry order.</summary>
        /// <returns>The descriptors.</returns>
        private IReadOnlyList<SettingKeyDescriptor> Build()
        {
            return [.. _declared.Select(Describe)];
        }

        /// <summary>A key as the settings editor needs it.</summary>
        /// <param name="declared">The key's declaration.</param>
        /// <returns>The descriptor.</returns>
        private SettingKeyDescriptor Describe(DeclaredSettingKey declared)
        {
            SettingKey key = declared.Key!;

            // --- The value's kind: a key with its own serialization contract is JSON; else its admitted form ---
            SettingValueType valueType = SettingValueType.Json;
            bool isArray = false;
            string? enumType = null;
            IReadOnlyList<string>? enumValues = null;
            if (!key.HasOwnContract && SettingValueShape.TryRead(key.ValueType, out SettingValueShape shape))
            {
                valueType = shape.ValueType;
                isArray = shape.IsArray;
                if (shape.ValueType == SettingValueType.Enum)
                {
                    enumType = shape.ScalarType.Name;
                    enumValues = EnumMembers(shape.ScalarType);
                }
            }

            // --- The description, only when a composed resource declares it ---
            string descriptionKey = key.LabelKey + DescriptionSuffix;
            return new SettingKeyDescriptor(
                key.Name,
                CategoryOf(key),
                GroupOf(key),
                key.Visibility,
                valueType,
                declared.IsNullable,
                isArray,
                enumType,
                enumValues,
                key.References is Type references ? _metadata.Get(references).Name : null,
                key.Visibility == SettingVisibility.Secret ? JsonNull : key.DefaultJson,
                key.LabelKey,
                _strings.Declares(descriptionKey) ? descriptionKey : null);
        }

        /// <summary>An enum's member names in declaration order.</summary>
        /// <param name="type">The enum.</param>
        /// <returns>The names.</returns>
        private static ImmutableArray<string> EnumMembers(Type type)
        {
            return [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(static field => field.MetadataToken).Select(static field => field.Name)];
        }
    }
}
