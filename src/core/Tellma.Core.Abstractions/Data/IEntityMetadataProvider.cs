// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The metadata of every mapped entity leaf of the tenant model, built and validated once at
    ///     startup. A singleton; a host whose model fails validation refuses to start, so a resolved
    ///     provider always describes a sound model.
    /// </summary>
    public interface IEntityMetadataProvider
    {
        /// <summary>
        ///     Every entity leaf, children included, in model order: each contributed entity followed by the
        ///     children its collections carry, then the entities only Core's model configurations add. The
        ///     platform's own migration tables are not entities and are absent.
        /// </summary>
        IReadOnlyList<EntityMetadata> All { get; }

        /// <summary>
        ///     The lower-case hex SHA-256 of the relational model the tenant databases must carry: every
        ///     table, its columns' names, store types, nullability, defaults and computed definitions, every
        ///     index, every sequence and every table type. Identical in the web host, the workers and the
        ///     migrator, which compose one model; bound on every batch so a database the migrator has not
        ///     brought to this model is refused.
        /// </summary>
        string StorageFingerprint { get; }

        /// <summary>
        ///     The metadata of an entity, given its leaf or any class the leaf derives from below the
        ///     platform's bases: <c>Get(typeof(User))</c> returns the metadata of <c>MyUser</c> when the
        ///     distribution registered that leaf for <c>User</c>.
        /// </summary>
        /// <param name="entityType">The leaf, the default it replaces, or another entity class in its chain.</param>
        /// <returns>The leaf's metadata.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entityType" /> is null.</exception>
        /// <exception cref="ArgumentException">No leaf of the model is or derives from <paramref name="entityType" />.</exception>
        [SuppressMessage(
            "Naming",
            "CA1716:Identifiers should not match keywords",
            Justification = "The provider's lookup is Get in every consumer; the platform's implementers are C#, where get is a contextual keyword only inside a property.")]
        EntityMetadata Get(Type entityType);
    }
}
