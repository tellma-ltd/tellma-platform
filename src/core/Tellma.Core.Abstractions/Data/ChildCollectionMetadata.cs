// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A child collection of an entity: a <c>[NotMapped]</c> list of a child entity, paired with the
    ///     child's parent key. Save and details payloads carry it; the model never sees it.
    /// </summary>
    /// <param name="Property">The collection's CLR property name, which is also its name on the wire.</param>
    /// <param name="Child">The metadata of the child's leaf.</param>
    /// <param name="ParentKey">The child's <see cref="ParentKeyAttribute" /> property, which references the parent.</param>
    /// <param name="MaxCount">The most rows one parent may carry in the collection (<see cref="MaxChildrenAttribute" />).</param>
    /// <param name="Getter">
    ///     Reads the collection of a parent instance: null when the payload did not supply it (leave the
    ///     children alone), empty when it supplied none (delete them all).
    /// </param>
    public sealed record ChildCollectionMetadata(
        string Property,
        EntityMetadata Child,
        PropertyMetadata ParentKey,
        int MaxCount,
        Func<object, IReadOnlyList<object>?> Getter);
}
