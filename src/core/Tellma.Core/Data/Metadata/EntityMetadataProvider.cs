// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>
    ///     The validated metadata of every entity leaf of one tenant model, with the lookup that resolves a
    ///     leaf from any class in its chain, and the model's storage fingerprint.
    /// </summary>
    /// <param name="all">Every leaf's metadata, in model order.</param>
    /// <param name="byType">Each leaf, and each class it derives from below the platform's bases, to the leaf's metadata.</param>
    /// <param name="storageFingerprint">The model's storage fingerprint.</param>
    internal sealed class EntityMetadataProvider(
        IReadOnlyList<EntityMetadata> all,
        IReadOnlyDictionary<Type, EntityMetadata> byType,
        string storageFingerprint) : IEntityMetadataProvider
    {
        private readonly FrozenDictionary<Type, EntityMetadata> _byType = byType.ToFrozenDictionary();

        /// <inheritdoc />
        public IReadOnlyList<EntityMetadata> All { get; } = [.. all];

        /// <inheritdoc />
        public string StorageFingerprint { get; } = storageFingerprint;

        /// <inheritdoc />
        public EntityMetadata Get(Type entityType)
        {
            ArgumentNullException.ThrowIfNull(entityType);
            return _byType.TryGetValue(entityType, out EntityMetadata? metadata)
                ? metadata
                : throw new ArgumentException(
                    $"{TypeDisplay.Qualified(entityType)} is not an entity of the tenant model: no mapped leaf is or derives from it.",
                    nameof(entityType));
        }
    }
}
