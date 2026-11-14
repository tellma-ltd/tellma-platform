// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>What a batch does when a cached input it used turns out to have moved under it.</summary>
    public enum VersionTagMismatchPolicy
    {
        /// <summary>
        ///     Discard the result and run the batch once more against the fresh input; a second
        ///     mismatch fails the request as stale. For inputs a decision rests on: permissions always,
        ///     and settings and lists on a validate or persist batch.
        /// </summary>
        Rerun,

        /// <summary>
        ///     Serve the result and refresh the cached input from the same round trip. For inputs whose
        ///     staleness a read can tolerate: settings, preferences and lists on a read batch.
        /// </summary>
        Refresh,
    }
}
