// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Supplies the bespoke criteria of one resource. A singleton; several may serve one resource,
    ///     and every one registered is consulted.
    /// </summary>
    /// <remarks>
    ///     <see cref="Criteria" /> is read once per evaluation and never awaited, so it returns trees
    ///     built once: constant, and the same for every caller.
    /// </remarks>
    [CollectionService]
    public interface IAccessCriteriaProvider
    {
        /// <summary>The registered resource the criteria apply to.</summary>
        string Resource { get; }

        /// <summary>The criteria: constant trees, the same for every caller.</summary>
        IReadOnlyList<AccessCriterion> Criteria { get; }
    }
}
