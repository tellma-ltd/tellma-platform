// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     A tenant setting a feature declares: its name, where it appears in the settings editor,
    ///     who may see it, and its code default. A tenant stores a row only for a value that departs
    ///     from the default. Declared as a <see cref="SettingKey{T}" /> static field.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The startup gate checks every registered key, so a bad declaration fails startup with
    ///         every problem listed rather than throwing from a static initializer:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>the name is dotted camelCase, <c>^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+$</c>, at most 128 characters, and unique across the composition;</item>
    ///         <item>the category is a single segment of the same grammar; <c>general</c>, without regard to case, shares the tenant's typed settings' securables;</item>
    ///         <item>a stated group is <c>^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*$</c>, at most 64 characters;</item>
    ///         <item>
    ///             the value type is <see cref="bool" />, <see cref="int" />, <see cref="long" />,
    ///             <see cref="decimal" />, <see cref="string" />, <see cref="DateOnly" />,
    ///             <see cref="TimeOnly" />, <see cref="Guid" />, an enum, a nullable form of one, or an
    ///             <see cref="System.Collections.Immutable.ImmutableArray{T}" /> of one (never a
    ///             <c>T[]</c>: the tenant's settings are shared by every request, so a value must not
    ///             be changeable, and an empty default is <c>[]</c>), unless the key supplies its own
    ///             serialization contract;
    ///         </item>
    ///         <item>a nullable key, and every <see cref="SettingVisibility.Secret" /> key, defaults to null;</item>
    ///         <item>
    ///             <see cref="References" /> names an <see cref="int" />-keyed stack entity and the value
    ///             type is <see cref="int" />, <c>int?</c> or <c>ImmutableArray&lt;int&gt;</c>; a secret key references
    ///             nothing.
    ///         </item>
    ///     </list>
    /// </remarks>
    public abstract record SettingKey
    {
        /// <summary>The prefix of every setting's label key.</summary>
        public const string LabelKeyPrefix = "Setting_";

        /// <summary>The longest a name may be.</summary>
        public const int MaxNameLength = 128;

        /// <summary>The longest a stated group may be.</summary>
        public const int MaxGroupLength = 64;

        /// <summary>Creates the key's common part; only <see cref="SettingKey{T}" /> derives.</summary>
        /// <param name="name">The name.</param>
        /// <param name="category">The category; null for the name's first segment.</param>
        /// <param name="group">The stated group; null to derive one from the name.</param>
        /// <param name="visibility">Who may see the value.</param>
        /// <param name="valueType">The value's type.</param>
        /// <param name="references">The entity an id value points at.</param>
        /// <param name="defaultJson">The default, serialized.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        private protected SettingKey(
            string name, string? category, string? group, SettingVisibility visibility, Type valueType, Type? references, JsonElement defaultJson)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            Name = name;
            Category = category ?? FirstSegment(name);
            Group = group;
            Visibility = visibility;
            ValueType = valueType;
            References = references;
            DefaultJson = defaultJson;
            LabelKey = LabelKeyPrefix + name.Replace('.', '_');
        }

        /// <summary>The key's name, dotted camelCase of at least two segments (<c>gl.posting.autoNumberOnPost</c>); the stored row's key.</summary>
        public string Name { get; }

        /// <summary>
        ///     The settings tab the key appears on, by default its name's first segment. Each category
        ///     is guarded by the securable <c>core.Settings.&lt;Category&gt;</c>, read and save.
        /// </summary>
        public string Category { get; }

        /// <summary>The card inside the category, as the declaration states it; null to derive one from the name (<see cref="DeriveGroup" />).</summary>
        public string? Group { get; }

        /// <summary>Who may see the value.</summary>
        public SettingVisibility Visibility { get; }

        /// <summary>The value's type.</summary>
        public Type ValueType { get; }

        /// <summary>
        ///     The top-level entity an id value points at, or a base whose registered leaf stands for
        ///     it. No foreign key backs it: a deleted target leaves an id the editor shows bare.
        /// </summary>
        public Type? References { get; }

        /// <summary>The code default, serialized once at construction under the platform's JSON rules.</summary>
        public JsonElement DefaultJson { get; }

        /// <summary>The label's resource key: <c>Setting_</c> and the name with each <c>.</c> as <c>_</c>.</summary>
        public string LabelKey { get; }

        /// <summary>
        ///     Reads a stored JSON value as the key's type and checks it, for code that holds keys
        ///     without their type (the settings loader, the editor's save).
        /// </summary>
        /// <param name="json">The stored value.</param>
        /// <param name="value">The value read, boxed; the key's default when it does not read or is refused.</param>
        /// <param name="validationCode">
        ///     The key's own validation code when the value reads but the key's check refuses it; null
        ///     otherwise, including when the value does not read as the key's type.
        /// </param>
        /// <returns>True when the value reads and the key's check accepts it.</returns>
        public abstract bool TryReadValue(JsonElement json, out object? value, out string? validationCode);

        /// <summary>
        ///     The group a key's name derives: the segments between the first and the last, less a
        ///     leading segment equal to the category. <c>gl.posting.autoNumberOnPost</c> derives
        ///     <c>posting</c>; <c>acme.invoicing.prefix</c> in category <c>invoicing</c> derives none,
        ///     and so does <c>gl.defaultCenterId</c>.
        /// </summary>
        /// <param name="name">The key's name.</param>
        /// <param name="category">The key's category.</param>
        /// <returns>The derived group, its segments joined by <c>.</c>; null when none remain.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name" /> or <paramref name="category" /> is null.</exception>
        public static string? DeriveGroup(string name, string category)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(category);

            // Two segments or fewer leave nothing between the first and the last.
            string[] segments = name.Split('.');
            if (segments.Length <= 2)
            {
                return null;
            }

            // The middle segments; a leading one that repeats the category says nothing more.
            IEnumerable<string> middle = segments[1..^1];
            if (string.Equals(segments[1], category, StringComparison.Ordinal))
            {
                middle = middle.Skip(1);
            }

            string group = string.Join('.', middle);
            return group.Length == 0 ? null : group;
        }

        /// <summary>The first segment of a dotted name.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The text before the first <c>.</c>, or the whole name.</returns>
        private static string FirstSegment(string name)
        {
            int dot = name.IndexOf('.', StringComparison.Ordinal);
            return dot < 0 ? name : name[..dot];
        }
    }
}
