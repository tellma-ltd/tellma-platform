// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Data.Batches
{
    // Spec 0015 §6.7: the persist re-declares the entity:* tags its validation rounds were served
    // under, so the pipeline reads what each round's batch declared once it composed.
    /// <summary>The platform's channel to the cached inputs a batch declared it depends on.</summary>
    internal interface IBatchDependencies
    {
        /// <summary>The dependencies declared so far, in declaration order.</summary>
        IReadOnlyList<VersionTagDependency> Dependencies { get; }
    }
}
