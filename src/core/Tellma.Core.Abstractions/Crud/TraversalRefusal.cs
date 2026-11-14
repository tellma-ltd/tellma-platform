// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>One path of a query the caller may not traverse: the entity the path reaches and the caller may not read.</summary>
    /// <param name="Entity">The entity name the caller's read decision refuses.</param>
    /// <param name="Path">The traversed path and the clause it appears in.</param>
    /// <exception cref="ArgumentException"><paramref name="Entity" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="Path" /> is null.</exception>
    public sealed record TraversalRefusal(string Entity, PathUse Path)
    {
        /// <inheritdoc cref="TraversalRefusal(string, PathUse)" />
        public string Entity { get; init => field = NotBlank(value); } = NotBlank(Entity);

        /// <inheritdoc cref="TraversalRefusal(string, PathUse)" />
        public PathUse Path { get; init => field = value ?? throw new ArgumentNullException(nameof(Path)); } =
            Path ?? throw new ArgumentNullException(nameof(Path));

        /// <summary>Refuses a blank entity name.</summary>
        /// <param name="entity">The name.</param>
        /// <returns>The name.</returns>
        private static string NotBlank(string entity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entity, nameof(Entity));
            return entity;
        }
    }
}
