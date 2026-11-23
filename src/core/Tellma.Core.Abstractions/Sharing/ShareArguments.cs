// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     The arguments of the <c>share</c> action: user grants merged into each target's grants.
    ///     Grants the arguments do not name are kept.
    /// </summary>
    /// <param name="Set">The user grants to add, or whose access to change; null for none.</param>
    /// <param name="Remove">The users whose grants to remove; null for none.</param>
    public sealed record ShareArguments(IReadOnlyList<UserGrant>? Set, IReadOnlyList<int>? Remove);
}
