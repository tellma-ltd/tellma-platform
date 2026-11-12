// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Realtime
{
    /// <summary>
    ///     Whether this host serves the real-time hub, which the client reads to decide whether to
    ///     connect or to poll. A singleton.
    /// </summary>
    public interface IRealtimeStatus
    {
        /// <summary>True when this host maps the hub; false in a host without it, or with it turned off in configuration.</summary>
        bool Enabled { get; }
    }
}
