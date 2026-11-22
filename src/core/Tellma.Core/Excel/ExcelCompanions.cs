// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;
using System.Reflection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Crud;
using Tellma.Core.Data;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The Excel feature's platform companions: <see cref="ExcelOperations{TEntity}" /> on every
    ///     stack that exports or imports, <see cref="ChildExportOperations{TChild}" /> on every
    ///     exportable child query surface, and the scoped registrations that build them. The
    ///     composition root hands this to the stack composition, which names no Excel type itself.
    /// </summary>
    internal sealed class ExcelCompanions : IPlatformCompanions
    {
        /// <summary>The operations that give a stack the Excel companion.</summary>
        private const StackOperations Workbooks = StackOperations.Export | StackOperations.Import;

        /// <summary>The name of the static factory each closed operations class declares.</summary>
        private const string FactoryName = "Create";

        /// <summary>The standard segments the Excel companion declares.</summary>
        private static readonly FrozenSet<string> WorkbookSegments = FrozenSet.Create(
            StringComparer.Ordinal,
            "export", "export/start", "export-for-import", "export-for-import/start", "inspect-import", "import", "import/start");

        /// <summary>Creates the companions; the composition root uses the one instance.</summary>
        private ExcelCompanions()
        {
        }

        /// <summary>The one instance.</summary>
        public static ExcelCompanions Instance { get; } = new();

        /// <inheritdoc />
        public IReadOnlySet<string> Segments => WorkbookSegments;

        /// <inheritdoc />
        public Type? CompanionOf(Type leaf, StackOperations operations)
        {
            ArgumentNullException.ThrowIfNull(leaf);
            return (operations & Workbooks) != 0 ? typeof(ExcelOperations<>).MakeGenericType(leaf) : null;
        }

        /// <inheritdoc />
        public bool IsCompanion(Type companion)
        {
            ArgumentNullException.ThrowIfNull(companion);
            return companion.IsConstructedGenericType && companion.GetGenericTypeDefinition() == typeof(ExcelOperations<>);
        }

        /// <inheritdoc />
        public Type ChildOperationsOf(Type child)
        {
            ArgumentNullException.ThrowIfNull(child);
            return typeof(ChildExportOperations<>).MakeGenericType(child);
        }

        /// <inheritdoc />
        /// <remarks>
        ///     One per child collection at any depth when the stack serves queries and exports, except
        ///     under a child type that opts out of its query surface, whose own children have none.
        /// </remarks>
        public IEnumerable<Type> ChildOperationsUnder(Type leaf, StackOperations operations, Func<Type, Type> leafOf)
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

            // Depth first: each child surface, then the surfaces under it; a cycle stops at its repeat.
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

                    handlers.Add(typeof(ChildExportOperations<>).MakeGenericType(child));
                    Collect(child, leafOf, visiting, handlers);
                }

                _ = visiting.Remove(parent);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Each class is built by its own static factory: its constructor takes the codec's
        ///     internal collaborators, which the container cannot see.
        /// </remarks>
        /// <exception cref="ArgumentException">The class declares no factory taking the scope's services.</exception>
        public ServiceDescriptor Scoped(Type closed)
        {
            ArgumentNullException.ThrowIfNull(closed);
            MethodInfo factory = closed.GetMethod(FactoryName, BindingFlags.NonPublic | BindingFlags.Static, [typeof(IServiceProvider)])
                ?? throw new ArgumentException($"The class {closed.Name} declares no internal static {FactoryName}(IServiceProvider).", nameof(closed));
            Func<IServiceProvider, object> create = factory.CreateDelegate<Func<IServiceProvider, object>>();
            return ServiceDescriptor.Scoped(closed, create);
        }
    }
}
