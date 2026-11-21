// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>Whether the control plane's admin surface is mapped, bound from <c>Tellma:Admin</c>.</summary>
    public sealed class AdminSurfaceOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Admin";

        /// <summary>
        ///     Whether the admin surface is mapped; null selects the identity mode's default: mapped
        ///     under a standalone identity server, unmapped in-proc, whose engine seeds no control-plane
        ///     client.
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>Whether the admin surface is mapped under an identity mode.</summary>
        /// <param name="mode">The identity mode.</param>
        /// <returns>True when the surface is mapped.</returns>
        public bool IsEnabled(IdentityMode mode)
        {
            return Enabled ?? (mode == IdentityMode.Standalone);
        }
    }
}
