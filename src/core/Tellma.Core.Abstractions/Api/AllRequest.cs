// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The body of a cacheable stack's <c>all</c>: every row in details shape, served from the cache.</summary>
    /// <param name="Include">The details extras to include, by name; an unknown name is a bad-request error. Null or empty for none.</param>
    public sealed record AllRequest(IReadOnlyList<string>? Include);
}
