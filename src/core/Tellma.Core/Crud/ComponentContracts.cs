// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Crud
{
    /// <summary>The contract each <see cref="ComponentKind" /> names, and how problems describe it.</summary>
    internal static class ComponentContracts
    {
        /// <summary>The open contract a component of a kind implements.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The open generic interface.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind" /> is undefined.</exception>
        public static Type Definition(ComponentKind kind)
        {
            return kind switch
            {
                ComponentKind.Validator => typeof(IEntityValidator<>),
                ComponentKind.PersistEffect => typeof(IPersistEffect<>),
                ComponentKind.DetailsContributor => typeof(IDetailsContributor<>),
                ComponentKind.NamedFilterContributor => typeof(INamedFilterContributor<>),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a component kind."),
            };
        }

        /// <summary>The contract a component of a kind implements for one entity.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="entity">The entity.</param>
        /// <returns>The closed interface.</returns>
        public static Type For(ComponentKind kind, Type entity)
        {
            return Definition(kind).MakeGenericType(entity);
        }

        /// <summary>How a problem names a component of a kind.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The words.</returns>
        public static string Describe(ComponentKind kind)
        {
            return kind switch
            {
                ComponentKind.Validator => "validator",
                ComponentKind.PersistEffect => "persist effect",
                ComponentKind.DetailsContributor => "details contributor",
                ComponentKind.NamedFilterContributor => "named filter contributor",
                _ => "component",
            };
        }
    }
}
