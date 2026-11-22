// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of <c>client/sync</c>: the version tags a browser tab holds for one tenant's cached
    ///     documents, each in its wire form, so the answer carries only what changed.
    /// </summary>
    /// <remarks>
    ///     The names are <c>settings</c>, <c>preferences</c>, <c>permissions</c>, <c>entities</c> and
    ///     one <c>entity:&lt;Name&gt;</c> per cacheable list the tab holds. Tags compare by ordinal
    ///     equality, and a name the server does not know is ignored. An empty map, or a body that omits
    ///     it, is a cold call that answers every member.
    /// </remarks>
    /// <param name="Held">The held wire tags by name; empty for a cold call.</param>
    public sealed record ClientSyncRequest(IReadOnlyDictionary<string, string> Held)
    {
        /// <inheritdoc cref="ClientSyncRequest(IReadOnlyDictionary{string, string})" />
        public IReadOnlyDictionary<string, string> Held { get; } = Held ?? FrozenDictionary<string, string>.Empty;
    }
}
