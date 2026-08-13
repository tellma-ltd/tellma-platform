// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>Fixed <see cref="ISandboxContext" /> implementations for hosts without tenants.</summary>
    public static class SandboxContext
    {
        /// <summary>Ambient work is never sandboxed (identity, landing, single-purpose hosts).</summary>
        /// <remarks>
        ///     The property type is <see cref="ISandboxContext" /> so that
        ///     <c>services.AddSingleton(SandboxContext.Never)</c> infers the service type a
        ///     tenant-less host actually needs registered.
        /// </remarks>
        public static ISandboxContext Never { get; } = new NeverSandboxContext();

        private sealed class NeverSandboxContext : ISandboxContext
        {
            public bool IsSandbox => false;
        }
    }
}
