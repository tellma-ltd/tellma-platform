// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Names the channel a user sends themselves a test message through: the body of the caller's
    ///     own test notification. The inbox cannot be tested; every other registered channel can.
    /// </summary>
    /// <param name="Channel">The key of a registered notification channel, such as <c>email</c>.</param>
    public sealed record TestNotificationRequest(string Channel)
    {
        /// <summary>The key of a registered notification channel, such as <c>email</c>.</summary>
        /// <exception cref="System.Text.Json.JsonException">The request names no channel.</exception>
        public string Channel { get; } = WireMembers.Required(Channel, "channel");
    }
}
