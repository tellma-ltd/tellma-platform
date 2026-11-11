// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     An entity's display-name rule: the property a row's display name renders, the multilingual group
    ///     when that property anchors one, and the prefix rendered before it.
    /// </summary>
    /// <remarks>
    ///     The source is the property <see cref="DisplayNameSourceAttribute" /> names; without the
    ///     declaration it is the entity's multilingual <c>Name</c> group, else its first natural key, else
    ///     its key. The display-name columns are the source, or every twin of its group a tenant's
    ///     content languages admit, plus the prefix.
    /// </remarks>
    /// <param name="Source">The property the display name renders; the group's anchor when the source is a group.</param>
    /// <param name="Group">The source's multilingual group when it anchors one; null for a scalar source.</param>
    /// <param name="Prefix">The property rendered before the source, when one is declared.</param>
    public sealed record DisplayNameMetadata(PropertyMetadata Source, MultilingualGroup? Group, PropertyMetadata? Prefix)
    {
        /// <summary>
        ///     What joins a prefix and a name, <c>" - "</c>. Never localized, and never inside a string
        ///     prefix's value, so a display name always splits at its first separator.
        /// </summary>
        public const string Separator = " - ";
    }
}
