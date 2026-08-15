// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>An email address with an optional display name.</summary>
    /// <param name="Address">The address ("someone@example.com").</param>
    /// <param name="DisplayName">The display name shown by mail clients, when known. Honoured on
    ///     recipients by every transport; on the sender it is transport-coupled, because a provider
    ///     may take the sender's display name from its own account configuration and refuse one
    ///     supplied per message.</param>
    public sealed record EmailAddress(string Address, string? DisplayName = null);
}
