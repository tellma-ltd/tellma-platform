// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     The classes the platform attaches to stacks beside the features' own: a companion on every
    ///     stack whose operations it serves, and the operations of each exportable child query
    ///     surface. The stack composition, registration and descriptors reach them only through here,
    ///     so the pipeline names none of the classes.
    /// </summary>
    internal interface IPlatformCompanions
    {
        /// <summary>The standard segments the platform's companion declares, which no other companion may take.</summary>
        IReadOnlySet<string> Segments { get; }

        /// <summary>The platform's companion of a stack, closed over its leaf.</summary>
        /// <param name="leaf">The stack's leaf.</param>
        /// <param name="operations">The stack's operations.</param>
        /// <returns>The closed class, or null when the stack's operations need none.</returns>
        Type? CompanionOf(Type leaf, StackOperations operations);

        /// <summary>Whether a companion is the platform's, closed over some leaf.</summary>
        /// <param name="companion">The companion's closed class.</param>
        /// <returns>True when it is.</returns>
        bool IsCompanion(Type companion);

        /// <summary>The operations of an exportable child query surface, closed over the child.</summary>
        /// <param name="child">The child entity's class, as the entity metadata maps it.</param>
        /// <returns>The closed class.</returns>
        Type ChildOperationsOf(Type child);

        /// <summary>The operations of every exportable child query surface under a stack.</summary>
        /// <param name="leaf">The stack's leaf.</param>
        /// <param name="operations">The stack's operations.</param>
        /// <param name="leafOf">Resolves a declared child type to the leaf the model maps.</param>
        /// <returns>The closed classes, depth first in collection order.</returns>
        IEnumerable<Type> ChildOperationsUnder(Type leaf, StackOperations operations, Func<Type, Type> leafOf);

        /// <summary>The scoped registration of one of the platform's closed classes.</summary>
        /// <param name="closed">The closed class: the companion or a child surface's operations.</param>
        /// <returns>The registration.</returns>
        ServiceDescriptor Scoped(Type closed);
    }
}
