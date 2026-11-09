// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>The answer to a versioned-cache read: the value, the tag it is current for, and how the read was served.</summary>
    /// <typeparam name="TValue">The cached value's type.</typeparam>
    /// <param name="Value">
    ///     The value, shared with every other reader and never to be changed. Null only when
    ///     <paramref name="Outcome" /> is <see cref="CacheOutcome.Oversized" />.
    /// </param>
    /// <param name="Tag">The tag the value is current for; <see cref="VersionTag.None" /> for an <see cref="CacheOutcome.Uncached" /> read, whose kind has no tag row.</param>
    /// <param name="Outcome">How the read was served.</param>
    public sealed record CacheResult<TValue>(TValue? Value, VersionTag Tag, CacheOutcome Outcome);
}
