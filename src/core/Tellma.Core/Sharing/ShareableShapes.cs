// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Sharing
{
    /// <summary>
    ///     The composition's shareable entities by resource name, each with its share entity and the
    ///     reference between them; built once, on first use, over the leaves the composition realised.
    /// </summary>
    internal sealed class ShareableShapes
    {
        /// <summary>The shapes by the shareable entity's name, built on first use.</summary>
        private readonly Lazy<IReadOnlyDictionary<string, ShareableShape>> _shapes;

        /// <summary>Creates the map.</summary>
        /// <param name="metadata">The entity metadata of the leaves.</param>
        /// <param name="pairs">The shareable leaves, each with its share leaf.</param>
        public ShareableShapes(IEntityMetadataProvider metadata, IReadOnlyList<(Type Entity, Type Share)> pairs)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(pairs);

            // The startup check proved every pair whole, so building each shape cannot fail here.
            _shapes = new(() => pairs
                .Select(pair => ShareableShape.Of(metadata, pair.Entity, pair.Share))
                .ToDictionary(static shape => shape.Entity.Name, StringComparer.Ordinal));
        }

        /// <summary>The shape of a shareable resource.</summary>
        /// <param name="resource">The resource name.</param>
        /// <returns>The shape; null when the resource is not a shareable entity.</returns>
        public ShareableShape? Find(string resource)
        {
            return _shapes.Value.GetValueOrDefault(resource);
        }
    }
}
