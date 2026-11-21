// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     Labels by convention, with no per-entity code, under the request's culture: for spreadsheet
    ///     headers, agent results and messages that name a field. Each lookup walks a chain of
    ///     resource keys across every composed feature's shared resources, then English, then a
    ///     humanized name; the browser application renders the same keys from its string pack.
    /// </summary>
    /// <remarks>
    ///     Each label method has a key-resolving twin returning the first key of its chain that the
    ///     English resources declare, or null when none does, which the client metadata emit writes
    ///     so the client never walks a chain.
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "A value set is what the parameter names; label providers are written in C#, where set is not a keyword.")]
    public interface ILabelProvider
    {
        /// <summary>An entity's label: <c>&lt;Schema&gt;_&lt;Entity&gt;</c>, or for the plural <c>…_Plural</c>, then the humanized class name.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="plural">Whether the plural label is wanted.</param>
        /// <returns>The label.</returns>
        string EntityLabel(Type entityType, bool plural = false);

        /// <summary>
        ///     A property's label: <c>&lt;Schema&gt;_&lt;Entity&gt;_&lt;Property&gt;</c>, then Core's shared
        ///     <c>&lt;Property&gt;</c>, then the humanized name. A multilingual twin carries its
        ///     language's symbol: <c>Name (E)</c>, <c>Name (ع)</c>.
        /// </summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="propertyName">The property.</param>
        /// <returns>The label.</returns>
        string PropertyLabel(Type entityType, string propertyName);

        /// <summary>A navigation's label (<c>Parent</c>, <c>CreatedBy</c>): its own key, its foreign key's chain, Core's shared key, the humanized name.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="navigation">The navigation.</param>
        /// <returns>The label.</returns>
        string NavigationLabel(Type entityType, string navigation);

        /// <summary>A child collection's label: its own key, then the child entity's plural label.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="collection">The collection.</param>
        /// <returns>The label.</returns>
        string CollectionLabel(Type entityType, string collection);

        /// <summary>An action's label (<c>take-over</c>, <c>preferences/set</c>): the entity's key, Core's shared <c>Action_&lt;Name&gt;</c>, the humanized name.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="action">The action's name.</param>
        /// <returns>The label.</returns>
        string ActionLabel(Type entityType, string action);

        /// <summary>A setting's label: <see cref="SettingKey.LabelKey" />, then its name's humanized last segment.</summary>
        /// <param name="key">The setting.</param>
        /// <returns>The label.</returns>
        string SettingLabel(SettingKey key);

        /// <summary>An enum member's label: <c>&lt;Schema&gt;_&lt;Enum&gt;_&lt;Value&gt;</c>, then the humanized value.</summary>
        /// <param name="enumType">The enum.</param>
        /// <param name="value">The member's name.</param>
        /// <returns>The label.</returns>
        string EnumValueLabel(Type enumType, string value);

        /// <summary>A value set's label for one of its values; the value itself when the set lacks it.</summary>
        /// <param name="set">The value set's name.</param>
        /// <param name="value">The value.</param>
        /// <returns>The label.</returns>
        string ValueLabel(string set, string value);

        /// <summary>The key <see cref="EntityLabel" /> resolves through; null when none is declared.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="plural">Whether the plural is wanted.</param>
        /// <returns>The key, or null.</returns>
        string? EntityLabelKey(Type entityType, bool plural = false);

        /// <summary>The key <see cref="PropertyLabel" /> resolves through (a twin's is its primary's); null when none is declared.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="propertyName">The property.</param>
        /// <returns>The key, or null.</returns>
        string? PropertyLabelKey(Type entityType, string propertyName);

        /// <summary>The key <see cref="NavigationLabel" /> resolves through; null when none is declared.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="navigation">The navigation.</param>
        /// <returns>The key, or null.</returns>
        string? NavigationLabelKey(Type entityType, string navigation);

        /// <summary>The key <see cref="CollectionLabel" /> resolves through; null when none is declared.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="collection">The collection.</param>
        /// <returns>The key, or null.</returns>
        string? CollectionLabelKey(Type entityType, string collection);

        /// <summary>The key <see cref="ActionLabel" /> resolves through; null when none is declared.</summary>
        /// <param name="entityType">The entity.</param>
        /// <param name="action">The action's name.</param>
        /// <returns>The key, or null.</returns>
        string? ActionLabelKey(Type entityType, string action);

        /// <summary>The key <see cref="SettingLabel" /> resolves through; null when none is declared.</summary>
        /// <param name="key">The setting.</param>
        /// <returns>The key, or null.</returns>
        string? SettingLabelKey(SettingKey key);

        /// <summary>The key <see cref="EnumValueLabel" /> resolves through; null when none is declared.</summary>
        /// <param name="enumType">The enum.</param>
        /// <param name="value">The member's name.</param>
        /// <returns>The key, or null.</returns>
        string? EnumValueLabelKey(Type enumType, string value);

        /// <summary>The key <see cref="ValueLabel" /> resolves through; null when none is declared, and for a literal label.</summary>
        /// <param name="set">The value set's name.</param>
        /// <param name="value">The value.</param>
        /// <returns>The key, or null.</returns>
        string? ValueLabelKey(string set, string value);
    }
}
