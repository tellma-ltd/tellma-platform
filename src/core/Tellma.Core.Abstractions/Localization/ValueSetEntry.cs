// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     One value of a value set and its label: either a resource key, rendered in the reader's
    ///     language, or a literal that reads the same in every language (a language's own name, a
    ///     time-zone id).
    /// </summary>
    /// <remarks>Exactly one of <paramref name="LabelKey" /> and <paramref name="Label" /> is set; the startup checks refuse an entry with both or neither.</remarks>
    /// <param name="Value">The stored value (<c>ar</c>, <c>gc</c>, <c>Africa/Addis_Ababa</c>, <c>SA</c>).</param>
    /// <param name="LabelKey">The resource key of the label (<c>Calendar_gc</c>, <c>Region_SA</c>); null for a literal label.</param>
    /// <param name="Label">The literal label; null for a keyed label.</param>
    public sealed record ValueSetEntry(string Value, string? LabelKey, string? Label);
}
