// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    /// <summary>One center of a development tenant's sample tree.</summary>
    /// <param name="Code">The center's code, which a re-run finds the stored center by.</param>
    /// <param name="Names">
    ///     The center's names by language. The first is the fallback for a tenant whose primary
    ///     language has no entry.
    /// </param>
    /// <param name="CenterType">The center's type.</param>
    /// <param name="ParentCode">The code of another center of the same list that is this one's parent; null for a root.</param>
    public sealed record SampleCenter(string Code, IReadOnlyList<SampleCenterName> Names, CenterType CenterType, string? ParentCode);
}
