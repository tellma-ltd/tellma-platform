// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>What a channel's test send to the caller came to.</summary>
    public enum TestNotificationResult
    {
        /// <summary>One test message was sent through the channel's transport.</summary>
        Sent,

        /// <summary>The caller's profile holds no address the channel can reach; nothing was sent.</summary>
        NoContactAddress,

        /// <summary>The channel's transport is not configured on this instance; nothing was sent.</summary>
        NotConfigured,
    }
}
