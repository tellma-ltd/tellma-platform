// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A details-extras component: produces the extras its class's <see cref="DetailsExtrasAttribute" />
    ///     names when a details read includes them.
    /// </summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    [CollectionService]
    public interface IDetailsContributor<TEntity>
    {
        /// <summary>Adds the statements or computed extras that produce the requested extras.</summary>
        /// <param name="plan">The details plan.</param>
        /// <param name="requested">The extras of this contributor the read includes; never empty.</param>
        void Contribute(DetailsPlan<TEntity> plan, IReadOnlySet<string> requested);
    }
}
