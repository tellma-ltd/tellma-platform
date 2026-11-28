// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Crud;
using Tellma.Core.Data;

namespace Tellma.Core.Blobs
{
    // spec 0017 §4.2: core's blob pair, added to every stack whose aggregate references a blob.
    /// <summary>
    ///     Adds <see cref="BlobReferenceValidator{TEntity}" /> and <see cref="BlobReferenceEffect{TEntity}" />,
    ///     closed over the leaf and attached by <c>core</c>, ahead of every other component of each
    ///     stack whose leaf or a child entity at any depth carries a <see cref="BlobReferenceAttribute" />
    ///     property, so the attach rule runs first among the validators and the blob statements first
    ///     among the effects.
    /// </summary>
    /// <remarks>
    ///     The stacks are settled before the model is built, so the aggregate is walked through its
    ///     classes as the metadata walks them: the leaf's properties, then each child collection's
    ///     leaf. The pair reads the model's metadata when it runs, so a property the model leaves out
    ///     costs a stack the pair's lookups and nothing more.
    /// </remarks>
    internal static class BlobReferenceComponents
    {
        /// <summary>Puts the pair at the head of the components of every stack whose aggregate references a blob.</summary>
        /// <param name="stacks">The settled stacks, their components attached.</param>
        /// <param name="record">The realised composition: its leaf substitutions and its features.</param>
        public static void Attach(IReadOnlyList<ComposedStack> stacks, TellmaCompositionRecord record)
        {
            ArgumentNullException.ThrowIfNull(stacks);
            ArgumentNullException.ThrowIfNull(record);

            foreach (ComposedStack stack in stacks)
            {
                if (stack.ServiceType is null || !References(stack.Leaf, record.LeafOf, []))
                {
                    continue;
                }

                // The platform's own feature attaches the pair; a composition without it is a test's,
                // and the stack's own feature stands in.
                ITellmaFeature owner = record.Features.FirstOrDefault(static feature => feature is CoreFeature) ?? stack.Owner;
                stack.Components.InsertRange(0,
                [
                    new ComposedComponent(ComponentKind.Validator, typeof(BlobReferenceValidator<>).MakeGenericType(stack.Leaf), owner),
                    new ComposedComponent(ComponentKind.PersistEffect, typeof(BlobReferenceEffect<>).MakeGenericType(stack.Leaf), owner),
                ]);
            }
        }

        /// <summary>Whether a class, or the leaf of a child collection it declares at any depth, has a blob-reference property.</summary>
        /// <param name="type">The class.</param>
        /// <param name="leafOf">Resolves an entity to the leaf registered for it.</param>
        /// <param name="visiting">The classes on the way down, which a self-nesting model would revisit.</param>
        /// <returns>True when one does.</returns>
        private static bool References(Type type, Func<Type, Type> leafOf, HashSet<Type> visiting)
        {
            if (!visiting.Add(type))
            {
                return false;
            }

            foreach (PropertyInfo property in EntityShapes.DeclaredProperties(type))
            {
                if (EntityShapes.GetAttribute<BlobReferenceAttribute>(property) is not null)
                {
                    return true;
                }
            }

            foreach ((PropertyInfo _, Type child) in EntityShapes.ChildCollections(type))
            {
                if (References(leafOf(child), leafOf, visiting))
                {
                    return true;
                }
            }

            _ = visiting.Remove(type);
            return false;
        }
    }
}
