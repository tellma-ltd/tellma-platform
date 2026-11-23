// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>The arguments of the <c>transfer</c> action.</summary>
    /// <param name="OwnerId">The new owner: an active individual member. Their own user grant, if any, is removed.</param>
    public sealed record TransferArguments([property: EntityRef("core.User")] int OwnerId);
}
