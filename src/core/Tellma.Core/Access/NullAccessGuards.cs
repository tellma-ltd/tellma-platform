// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Access
{
    // The guards are spec 0014 §8.3; this default holds their seat in the persist pipeline until the
    // guards themselves are registered in its place.
    /// <summary>The security tables' guards that contribute nothing: no lock and no assertion.</summary>
    internal sealed class NullAccessGuards : IAccessGuards
    {
        /// <inheritdoc />
        public void ContributeLock(IDataBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
        }

        /// <inheritdoc />
        public void ContributeInvariants(IDataBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
        }
    }
}
