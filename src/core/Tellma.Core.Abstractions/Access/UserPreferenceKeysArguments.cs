// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The keys a delete removes from a preference bag: the body of the caller's own delete, and
    ///     the arguments of an administrator's delete from another user's bag. A key the bag does not
    ///     hold is no error.
    /// </summary>
    /// <param name="Keys">The keys, each once.</param>
    public sealed record UserPreferenceKeysArguments(IReadOnlyList<string> Keys)
    {
        /// <summary>The keys, each once.</summary>
        /// <exception cref="System.Text.Json.JsonException">The request carries no keys.</exception>
        public IReadOnlyList<string> Keys { get; } = WireMembers.Required(Keys, "keys");
    }
}
