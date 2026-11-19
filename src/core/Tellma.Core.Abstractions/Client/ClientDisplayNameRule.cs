// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     How a row's display name is built, as <see cref="DisplayNameMetadata" /> declares it: the
    ///     source property, whether it is a multilingual group read in the active slot, and the property
    ///     shown before it with the fixed separator.
    /// </summary>
    /// <param name="Source">The property the name reads, such as <c>Name</c>; a multilingual group's anchor names the whole group.</param>
    /// <param name="Multilingual">Whether the source is a multilingual group.</param>
    /// <param name="Prefix">The property shown before the name, such as <c>Code</c>; null, and omitted, for none.</param>
    public sealed record ClientDisplayNameRule(string Source, bool Multilingual, string? Prefix);
}
