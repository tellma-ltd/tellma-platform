// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>One child collection of a stack's entity, as the save and details paths see it.</summary>
    /// <param name="Property">The collection property on the owner (<c>Lines</c>).</param>
    /// <param name="ChildType">The child entity's type.</param>
    /// <param name="ParentKeyProperty">The child's property holding the owner's id (<c>InvoiceId</c>).</param>
    /// <param name="MaxCount">The rows the collection may hold per owner, enforced at every depth.</param>
    public sealed record ChildCollectionDescriptor(string Property, Type ChildType, string ParentKeyProperty, int MaxCount);
}
