// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>The multilingual group a property fills, and its slot in the group.</summary>
    /// <param name="Group">The group's name, its anchor's: <c>Name</c>.</param>
    /// <param name="Slot">The slot: 1 for the anchor, 2 and 3 for its twins.</param>
    public sealed record ClientMultilingualSlot(string Group, int Slot);
}
