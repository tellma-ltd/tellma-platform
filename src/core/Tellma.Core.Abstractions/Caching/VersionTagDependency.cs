// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     A cached input a batch was composed from, declared to the batch. After a read or validate
    ///     batch the executor compares the tag the batch read with <see cref="ExpectedTag" /> and
    ///     applies <see cref="OnMismatch" />; on a persist batch the name also enters the
    ///     in-transaction guard, so no write commits under a moved input.
    /// </summary>
    /// <param name="Name">The tenant-level tag name the input is cached under (<see cref="VersionTagNames" />).</param>
    /// <param name="ExpectedTag">The tag the cached input carries.</param>
    /// <param name="OnMismatch">What to do when the database holds another tag.</param>
    public sealed record VersionTagDependency(string Name, VersionTag ExpectedTag, VersionTagMismatchPolicy OnMismatch);
}
