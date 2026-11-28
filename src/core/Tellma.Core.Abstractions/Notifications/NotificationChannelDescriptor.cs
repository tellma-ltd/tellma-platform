// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     One way a notification reaches its recipient: <c>inbox</c> and <c>email</c> in every
    ///     distribution, and any a module or distribution registers with its sender. A channel that is
    ///     not registered does not exist on the instance.
    /// </summary>
    /// <remarks>
    ///     <see cref="Key" /> follows <c>^[a-z][a-z0-9-]*$</c>, at most 16 characters; its label is
    ///     the resource <c>NotificationChannel_&lt;Key&gt;</c>. The composition refuses at startup a key
    ///     declared twice or one that breaks the grammar.
    /// </remarks>
    /// <param name="Key">The channel's key.</param>
    /// <param name="DefaultEnabled">Whether the channel is on for a user who saved no preference for it.</param>
    public sealed record NotificationChannelDescriptor(string Key, bool DefaultEnabled);
}
