// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Testing.Support.Tenancy
{
    /// <summary>An <see cref="ISandboxContext" /> a test sets directly.</summary>
    /// <param name="isSandbox">Whether the ambient work is to be treated as a sandbox tenant's.</param>
    public sealed class StubSandboxContext(bool isSandbox = false) : ISandboxContext
    {
        /// <inheritdoc />
        public bool IsSandbox { get; set; } = isSandbox;
    }
}
