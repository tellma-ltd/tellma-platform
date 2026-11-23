// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     The arguments of the <c>publish</c> action: role grants and the Everyone grant merged into
    ///     each target's grants. Grants the arguments do not name are kept.
    /// </summary>
    /// <param name="Set">The role grants to add, or whose access to change; null for none.</param>
    /// <param name="Remove">The roles whose grants to remove; null for none.</param>
    /// <param name="Everyone">True adds the Everyone grant, at view access; false removes it; null leaves it as it is.</param>
    public sealed record PublishArguments(IReadOnlyList<RoleGrant>? Set, IReadOnlyList<int>? Remove, bool? Everyone);
}
