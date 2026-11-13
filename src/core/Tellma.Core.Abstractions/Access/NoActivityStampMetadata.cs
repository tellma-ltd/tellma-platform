// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Endpoint metadata marking a polling endpoint, whose calls do not count as the user's
    ///     activity: the connect prologue leaves the user's last-active stamp as it is.
    /// </summary>
    /// <remarks>
    ///     An endpoint a client calls on a timer rather than on a person's action would otherwise keep
    ///     an idle user active for as long as a tab stays open.
    /// </remarks>
    public sealed record NoActivityStampMetadata;
}
