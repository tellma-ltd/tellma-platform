// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The body of <c>access/holders</c>: who holds one securable, and through which grants.</summary>
    /// <param name="Resource">The securable's resource, such as <c>gl.Center</c>.</param>
    /// <param name="Action">The securable's action, such as <c>Save</c>.</param>
    public sealed record AccessHoldersRequest(string Resource, string Action);
}
