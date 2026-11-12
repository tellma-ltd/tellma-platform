// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.Notifications
{
    /// <summary>The real-time status of a host without the hub: never enabled. A host that maps the hub replaces it.</summary>
    internal sealed class NullRealtimeStatus : IRealtimeStatus
    {
        /// <inheritdoc />
        public bool Enabled => false;
    }
}
