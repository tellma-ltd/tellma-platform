// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A unique index of an entity's table, by the name a duplicate-key error reports it under, so the
    ///     error maps back to the properties it covers.
    /// </summary>
    /// <param name="IndexName">The index's database name, such as <c>UX_Centers_Code</c>.</param>
    /// <param name="Properties">The indexed properties, in key order; a child's parent key leads a per-parent index.</param>
    public sealed record UniqueIndexMetadata(string IndexName, IReadOnlyList<PropertyMetadata> Properties);
}
