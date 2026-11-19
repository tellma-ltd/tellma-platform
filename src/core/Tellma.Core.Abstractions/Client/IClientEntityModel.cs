// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     The tenant-independent entity model the client metadata carries: every entity the query
    ///     language knows, with its properties, navigations, children, actions and limits and the keys
    ///     of its labels resolved, and every named enum the entities, the setting keys and the action
    ///     arguments name. The platform registers it as a singleton, built once from the realised
    ///     composition on first use.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same composition always yields the same model: the entities are ordered by name, each
    ///         entity's members keep the order of its metadata, and the enums are ordered by name.
    ///     </para>
    ///     <para>
    ///         The model holds no tenant fact and no physical name: neither the tenant's languages nor its
    ///         settings, and neither tables, store types nor schema fingerprints.
    ///     </para>
    /// </remarks>
    public interface IClientEntityModel
    {
        /// <summary>Every entity, ordered by name, ordinal.</summary>
        IReadOnlyList<ClientEntityMetadata> Entities { get; }

        /// <summary>Every named enum, each once, ordered by name, ordinal: the C# enums and the value sets.</summary>
        IReadOnlyList<ClientEnumMetadata> Enums { get; }

        /// <summary>An entity by its name, compared ordinally without regard to case.</summary>
        /// <param name="entity">The entity's name, such as <c>gl.Center</c>.</param>
        /// <returns>The entity's metadata, or null when the model has no such entity.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entity" /> is null.</exception>
        ClientEntityMetadata? Find(string entity);
    }
}
