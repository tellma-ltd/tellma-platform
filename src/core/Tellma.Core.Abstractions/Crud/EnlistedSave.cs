// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>A save enlisted with a host: the target rows, which carry their final ids once the enlistment completed and the batch's stamp once the host committed.</summary>
    /// <typeparam name="TEntity">The target stack's entity type.</typeparam>
    public sealed class EnlistedSave<TEntity>
    {
        /// <summary>Records an enlisted save.</summary>
        /// <param name="rows">The target rows, the writer's own instances.</param>
        /// <param name="resource">The target stack's entity name.</param>
        /// <exception cref="ArgumentNullException"><paramref name="rows" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="resource" /> is null, empty or white space.</exception>
        internal EnlistedSave(IReadOnlyList<TEntity> rows, string resource)
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            Rows = rows;
            Resource = resource;
        }

        /// <summary>The target rows: final ids once the enlistment completed, stamped after the host committed.</summary>
        public IReadOnlyList<TEntity> Rows { get; }

        /// <summary>The target stack's entity name.</summary>
        public string Resource { get; }
    }
}
