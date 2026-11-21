// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>The system short-circuit: the system set holds every securable, unrestricted.</summary>
    /// <param name="Resource">The decided securable's resource.</param>
    /// <param name="Action">The decided securable's action.</param>
    public sealed record SystemAccessGrant(string Resource, string Action) : AccessGrant(Resource, Action);
}
