// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    /// <summary>A sample center's name in one language.</summary>
    /// <param name="Language">The language subtag, such as <c>en</c> or <c>ar</c>, matched against the tenant's content languages.</param>
    /// <param name="Text">The name in that language.</param>
    public sealed record SampleCenterName(string Language, string Text);
}
