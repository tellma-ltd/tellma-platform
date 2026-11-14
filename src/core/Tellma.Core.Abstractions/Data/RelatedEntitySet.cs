// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The related instances of one entity a read carries: partial instances holding only the columns
    ///     of <see cref="Projection" />, never assigned to a navigation and never written back by a save.
    /// </summary>
    /// <param name="EntityType">The CLR type of the instances: the entity's leaf.</param>
    /// <param name="Entities">The instances.</param>
    /// <param name="Projection">The property names every instance carries; a path through the set may read only these.</param>
    public sealed record RelatedEntitySet(Type EntityType, IReadOnlyList<object> Entities, IReadOnlySet<string> Projection);
}
