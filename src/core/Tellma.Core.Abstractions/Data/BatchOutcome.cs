// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>What an executed batch leaves behind besides its statements' results.</summary>
    /// <param name="VersionTags">
    ///     The tenant's version-tag snapshot after the batch: the one the batch's prelude read, with
    ///     its own bumps overlaid, or the process's current snapshot when the batch read none.
    /// </param>
    /// <param name="UserVersionTags">
    ///     The caller's user-level tags the connect prologue read; null on a batch without a connect
    ///     prologue.
    /// </param>
    /// <param name="RoundTrips">The round trips the batch took, the retries included.</param>
    public sealed record BatchOutcome(VersionTagSnapshot VersionTags, UserVersionTagSnapshot? UserVersionTags, int RoundTrips);
}
