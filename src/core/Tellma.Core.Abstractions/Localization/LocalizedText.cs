// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     A text in each of a tenant's three content languages, as an entity's <c>Name</c>,
    ///     <c>Name2</c> and <c>Name3</c> hold it: the value of an argument a bilingual tenant needs in
    ///     each content language, such as the name of the record a notification is about.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On the wire, under any serializer options, it is a JSON array of exactly three elements:
    ///         the first a string, the others strings or <c>null</c> (<c>["Acme", "أكمي", null]</c>).
    ///         No other argument has that shape, so a reader of stored arguments tells it apart.
    ///     </para>
    ///     <para>
    ///         The values are a snapshot taken when the text was built: a record renamed later keeps
    ///         the old text. The slots follow the tenant's content languages, so a later change of
    ///         those languages relabels them as it relabels <c>Name2</c>.
    ///     </para>
    /// </remarks>
    /// <param name="Text">The text in the tenant's first content language, never <see langword="null" />.</param>
    /// <param name="Text2">The text in the second content language, or <see langword="null" />.</param>
    /// <param name="Text3">The text in the third content language, or <see langword="null" />.</param>
    [JsonConverter(typeof(LocalizedTextJsonConverter))]
    public sealed record LocalizedText(string Text, string? Text2 = null, string? Text3 = null)
    {
        /// <summary>The anchor and twins of each multilingual group <see cref="Names" /> has read, by entity type and anchor name.</summary>
        private static readonly ConcurrentDictionary<(Type Type, string Property), PropertyInfo[]> Groups = new();

        /// <summary>The text in the tenant's first content language, never <see langword="null" />.</summary>
        /// <remarks>Get-only, so a <see langword="with" /> expression cannot bypass the constructor's check.</remarks>
        /// <exception cref="ArgumentNullException">Built with <see langword="null" />.</exception>
        public string Text { get; } = Text ?? throw new ArgumentNullException(nameof(Text));

        /// <summary>
        ///     Copies the multilingual group of an entity's property: the anchor <c>P</c>, declared with
        ///     <see cref="MultilingualAttribute" />, and its twins <c>P2</c> and <c>P3</c>. The group is
        ///     read by reflection and cached per type.
        /// </summary>
        /// <param name="entity">The entity, such as a loaded user or record.</param>
        /// <param name="property">The anchor's name; <c>Name</c> by default.</param>
        /// <returns>The group's three texts.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entity" /> or <paramref name="property" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The entity's type has no multilingual string anchor of that name with its two string twins,
        ///     or the anchor holds no text.
        /// </exception>
        public static LocalizedText Names(object entity, string property = "Name")
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(property);

            // --- The group's three properties, resolved once per type and anchor ---
            PropertyInfo[] group = Groups.GetOrAdd((entity.GetType(), property), static key => ResolveGroup(key.Type, key.Property));

            // --- The snapshot: the anchor must hold text; a twin may be empty ---
            string text = group[0].GetValue(entity) as string ?? throw new ArgumentException(
                $"The {group[0].Name} of the {entity.GetType().Name} holds no text, so it cannot be the first slot of a localized text.",
                nameof(entity));

            return new LocalizedText(text, group[1].GetValue(entity) as string, group[2].GetValue(entity) as string);
        }

        /// <summary>
        ///     The text in one content slot: that slot's text, else the first slot's when the slot is
        ///     <see langword="null" /> or empty. The renderer passes the reader's
        ///     <see cref="LocalizationContext.ContentLanguageIndex" />.
        /// </summary>
        /// <param name="contentLanguageIndex">The slot, from 1 to 3.</param>
        /// <returns>The text.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="contentLanguageIndex" /> is not 1, 2 or 3.</exception>
        public string In(int contentLanguageIndex)
        {
            return contentLanguageIndex switch
            {
                1 => Text,
                2 => string.IsNullOrEmpty(Text2) ? Text : Text2,
                3 => string.IsNullOrEmpty(Text3) ? Text : Text3,
                _ => throw new ArgumentOutOfRangeException(nameof(contentLanguageIndex), contentLanguageIndex, "A content slot is 1, 2 or 3."),
            };
        }

        /// <summary>Finds a type's multilingual anchor and its twins.</summary>
        /// <param name="type">The entity's type.</param>
        /// <param name="property">The anchor's name.</param>
        /// <returns>The anchor, then its second and third twins.</returns>
        /// <exception cref="ArgumentException">The anchor or a twin is missing, is not a string, or the anchor is not multilingual.</exception>
        private static PropertyInfo[] ResolveGroup(Type type, string property)
        {
            PropertyInfo anchor = StringProperty(type, property);

            // Read through Attribute.GetCustomAttribute, which walks overridden properties.
            return Attribute.GetCustomAttribute(anchor, typeof(MultilingualAttribute), inherit: true) is null
                ? throw new ArgumentException(
                    $"The property {type.Name}.{property} is not declared [Multilingual], so it has no group to copy.",
                    nameof(property))
                :
                [
                    anchor,
                    StringProperty(type, property + MultilingualAttribute.SecondarySuffix),
                    StringProperty(type, property + MultilingualAttribute.TertiarySuffix),
                ];
        }

        /// <summary>A public, readable string instance property of a type.</summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>The property.</returns>
        /// <exception cref="ArgumentException">The type has no such property.</exception>
        private static PropertyInfo StringProperty(Type type, string name)
        {
            PropertyInfo? found = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return found is { CanRead: true } && found.PropertyType == typeof(string) && found.GetIndexParameters().Length == 0
                ? found
                : throw new ArgumentException(
                    $"The type {type.Name} has no readable string property {name}, which a multilingual group needs.",
                    nameof(type));
        }
    }
}
