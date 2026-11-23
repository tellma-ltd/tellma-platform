// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Sharing
{
    // Spec 0033 §2.1: a share entity has no route, no child query surface and no client metadata,
    // so the client entity model leaves these classes out.
    /// <summary>The registered leaves of every shareable's share entity, which the client metadata does not describe.</summary>
    /// <param name="Types">The share entities' registered leaves.</param>
    internal sealed record ShareEntityTypes(IReadOnlySet<Type> Types);
}
