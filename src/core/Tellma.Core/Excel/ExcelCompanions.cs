// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Data;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Where the stack composition meets the Excel operations: which stacks carry the platform's
    ///     companion, the classes closed over a stack's leaf and its exportable child entities, and the
    ///     scoped registrations that build them. The pipeline knows the operations only through here.
    /// </summary>
    internal static class ExcelCompanions
    {
        /// <summary>The operations that give a stack the Excel companion.</summary>
        private const StackOperations Workbooks = StackOperations.Export | StackOperations.Import;

        /// <summary>The name of the static factory each closed operations class declares.</summary>
        private const string FactoryName = "Create";

        /// <summary>Whether a stack with these operations carries the Excel companion: it exports or imports.</summary>
        /// <param name="operations">The stack's operations.</param>
        /// <returns>True when it does.</returns>
        public static bool Serves(StackOperations operations)
        {
            return (operations & Workbooks) != 0;
        }

        /// <summary>The Excel companion closed over a stack's leaf.</summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The closed class.</returns>
        public static Type OperationsOf(Type leaf)
        {
            ArgumentNullException.ThrowIfNull(leaf);
            return typeof(ExcelOperations<>).MakeGenericType(leaf);
        }

        /// <summary>Whether a companion is the platform's Excel companion, closed over some leaf.</summary>
        /// <param name="companion">The companion's closed class.</param>
        /// <returns>True when it is.</returns>
        public static bool IsOperations(Type companion)
        {
            ArgumentNullException.ThrowIfNull(companion);
            return companion.IsConstructedGenericType && companion.GetGenericTypeDefinition() == typeof(ExcelOperations<>);
        }

        /// <summary>The child export operations closed over a child entity.</summary>
        /// <param name="child">The child entity's class, as the entity metadata maps it.</param>
        /// <returns>The closed class.</returns>
        public static Type ChildOperationsOf(Type child)
        {
            ArgumentNullException.ThrowIfNull(child);
            return typeof(ChildExportOperations<>).MakeGenericType(child);
        }

        /// <summary>
        ///     The child export operations of every exportable child query surface under a stack: one
        ///     per child collection at any depth when the stack serves queries and exports, except
        ///     under a child type that opts out of its query surface, whose own children have none.
        /// </summary>
        /// <param name="leaf">The stack's leaf.</param>
        /// <param name="operations">The stack's operations.</param>
        /// <param name="leafOf">Resolves a declared child type to the leaf the model maps.</param>
        /// <returns>The closed classes, depth first in collection order.</returns>
        public static IEnumerable<Type> ChildOperationsUnder(Type leaf, StackOperations operations, Func<Type, Type> leafOf)
        {
            ArgumentNullException.ThrowIfNull(leaf);
            ArgumentNullException.ThrowIfNull(leafOf);
            if (!operations.HasFlag(StackOperations.Query) || !operations.HasFlag(StackOperations.Export))
            {
                return [];
            }

            List<Type> handlers = [];
            Collect(leaf, leafOf, [], handlers);
            return handlers;

            static void Collect(Type parent, Func<Type, Type> leafOf, HashSet<Type> visiting, List<Type> handlers)
            {
                if (!visiting.Add(parent))
                {
                    return;
                }

                foreach ((PropertyInfo _, Type declared) in EntityShapes.ChildCollections(parent))
                {
                    Type child = leafOf(declared);
                    if (EntityShapes.GetAttribute<ApiResourceAttribute>(child) is { Queryable: false })
                    {
                        continue;
                    }

                    handlers.Add(ChildOperationsOf(child));
                    Collect(child, leafOf, visiting, handlers);
                }

                _ = visiting.Remove(parent);
            }
        }

        /// <summary>
        ///     The scoped registration of a closed operations class, built by its own static factory:
        ///     the class's constructor takes the codec's internal collaborators, which the container
        ///     cannot see.
        /// </summary>
        /// <param name="closed">The closed class: an Excel companion or a child export operations class.</param>
        /// <returns>The registration.</returns>
        /// <exception cref="ArgumentException">The class declares no factory taking the scope's services.</exception>
        public static ServiceDescriptor Scoped(Type closed)
        {
            ArgumentNullException.ThrowIfNull(closed);
            MethodInfo factory = closed.GetMethod(FactoryName, BindingFlags.NonPublic | BindingFlags.Static, [typeof(IServiceProvider)])
                ?? throw new ArgumentException($"The class {closed.Name} declares no internal static {FactoryName}(IServiceProvider).", nameof(closed));
            Func<IServiceProvider, object> create = factory.CreateDelegate<Func<IServiceProvider, object>>();
            return ServiceDescriptor.Scoped(closed, create);
        }
    }
}
