// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The preferences a set writes into a preference bag: the body of the caller's own set, and
    ///     the arguments of an administrator's set of another user's bag. Keys the items do not name
    ///     are kept.
    /// </summary>
    /// <param name="Items">The preferences, each key once.</param>
    public sealed record UserPreferencesArguments(IReadOnlyList<KeyValueItem> Items)
    {
        /// <summary>The preferences, each key once.</summary>
        /// <exception cref="System.Text.Json.JsonException">The request carries no items.</exception>
        public IReadOnlyList<KeyValueItem> Items { get; } = WireMembers.Required(Items, "items");
    }
}
