// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>A delete enlisted with a host: the target ids, deleted when the host commits.</summary>
    /// <typeparam name="TKey">The target stack's key type.</typeparam>
    public sealed class EnlistedDelete<TKey>
    {
        /// <summary>Records an enlisted delete.</summary>
        /// <param name="ids">The target ids.</param>
        /// <param name="resource">The target stack's entity name.</param>
        /// <exception cref="ArgumentNullException"><paramref name="ids" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="resource" /> is null, empty or white space.</exception>
        internal EnlistedDelete(IReadOnlyList<TKey> ids, string resource)
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            Ids = ids;
            Resource = resource;
        }

        /// <summary>The target ids.</summary>
        public IReadOnlyList<TKey> Ids { get; }

        /// <summary>The target stack's entity name.</summary>
        public string Resource { get; }
    }
}
