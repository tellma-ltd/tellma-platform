// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A multilingual text group: an anchor <c>P</c> declared with <see cref="MultilingualAttribute" />
    ///     and its twins <c>P2</c> and <c>P3</c>, which hold the same text in the tenant's second and third
    ///     content languages.
    /// </summary>
    /// <param name="Name">The group's name: its anchor's property name.</param>
    /// <param name="Primary">The anchor, the first content language's text.</param>
    /// <param name="Secondary">The twin <c>P2</c>, the second content language's text.</param>
    /// <param name="Tertiary">The twin <c>P3</c>, the third content language's text.</param>
    public sealed record MultilingualGroup(
        string Name, PropertyMetadata Primary, PropertyMetadata? Secondary, PropertyMetadata? Tertiary);
}
