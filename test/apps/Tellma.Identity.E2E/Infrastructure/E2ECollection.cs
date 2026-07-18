// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>The collection every browser scenario joins to share the host and browser.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class E2ECollectionDefinition : ICollectionFixture<PlaywrightFixture>
    {
        /// <summary>The collection name used by <c>[Collection]</c> attributes.</summary>
        public const string Name = "E2E";
    }
}
