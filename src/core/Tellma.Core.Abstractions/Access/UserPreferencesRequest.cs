// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>Names the user whose preferences an administrator reads.</summary>
    /// <param name="UserId">The user's id.</param>
    public sealed record UserPreferencesRequest(int UserId);
}
