// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     What the link builder needs to know of a resource: the route segment of its stack and the
    ///     operations the stack serves. The stack registry answers it; until a composition registers
    ///     one, <see cref="NoStackRoutes" /> answers that no resource names a stack. A singleton.
    /// </summary>
    internal interface IStackRouteSource
    {
        /// <summary>The route of the stack a resource names.</summary>
        /// <param name="resource">The resource, such as <c>acme.SalesInvoice</c>.</param>
        /// <returns>The stack's route, or null when the resource names no stack.</returns>
        StackRoute? Find(string resource);
    }

    /// <summary>The part of a stack a link is built from.</summary>
    /// <param name="ResourceSegment">The stack's route segment, such as <c>sales-invoices</c>.</param>
    /// <param name="Operations">The operations the stack serves.</param>
    internal readonly record struct StackRoute(string ResourceSegment, StackOperations Operations);

    /// <summary>The route source of a composition with no stack registry: no resource names a stack.</summary>
    internal sealed class NoStackRoutes : IStackRouteSource
    {
        /// <inheritdoc />
        public StackRoute? Find(string resource)
        {
            return null;
        }
    }
}
