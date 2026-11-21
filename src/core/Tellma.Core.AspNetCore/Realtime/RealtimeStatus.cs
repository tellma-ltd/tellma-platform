// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>Reports whether this host serves the hub: whether <see cref="RealtimeOptions.Enabled" /> is set.</summary>
    /// <param name="options">The realtime options.</param>
    internal sealed class RealtimeStatus(IOptions<RealtimeOptions> options) : IRealtimeStatus
    {
        /// <inheritdoc />
        public bool Enabled => options.Value.Enabled;
    }
}
