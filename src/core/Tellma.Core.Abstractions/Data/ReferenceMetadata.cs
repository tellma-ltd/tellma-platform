// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A many-to-one foreign key of an entity, and the navigation queries reach its target through:
    ///     a <c>[References]</c> property, a child's parent key, an audit column, a tree's parent, a job
    ///     or a blob column.
    /// </summary>
    /// <param name="Navigation">
    ///     The navigation's name: the model's navigation when one exists, else the foreign-key property's
    ///     name without its <c>Id</c> suffix (<c>ParentId</c> is <c>Parent</c>, <c>CreatedById</c> is
    ///     <c>CreatedBy</c>).
    /// </param>
    /// <param name="ForeignKey">The foreign-key property.</param>
    /// <param name="Target">The metadata of the referenced entity's leaf.</param>
    public sealed record ReferenceMetadata(string Navigation, PropertyMetadata ForeignKey, EntityMetadata Target);
}
