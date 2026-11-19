// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>The owner of a child entity, and the navigation and collection that link the two.</summary>
    /// <param name="Entity">The owning entity's name, such as <c>acme.Customer</c>.</param>
    /// <param name="Navigation">The child's navigation to its owner, such as <c>Customer</c>.</param>
    /// <param name="Collection">The owner's collection of the children, such as <c>Contacts</c>.</param>
    public sealed record ClientEntityOwner(string Entity, string Navigation, string Collection);
}
