// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     One tenant of the registry's snapshot: its descriptor, its location, the connection string
    ///     composed for it when the snapshot was loaded, its non-secret properties and the row
    ///     version the snapshot was loaded from.
    /// </summary>
    /// <remarks>
    ///     The connection string is the process's only copy of the tenant's composed string, and is
    ///     never logged: <see cref="ToString" /> leaves it out, so a tenant logged by accident logs
    ///     everything but the string.
    /// </remarks>
    /// <param name="Descriptor">What the platform shows about the tenant.</param>
    /// <param name="Location">Where the tenant's database lives.</param>
    /// <param name="ConnectionString">The composed connection string; never logged.</param>
    /// <param name="Properties">The row's non-secret, distribution-defined JSON, or null.</param>
    /// <param name="Version">The row version the snapshot holds.</param>
    public sealed record TenantInfo(
        TenantDescriptor Descriptor,
        TenantLocation Location,
        string ConnectionString,
        string? Properties,
        Guid Version)
    {
        /// <summary>The tenant's id, the descriptor's.</summary>
        public int Id => Descriptor.Id;

        /// <summary>
        ///     Renders every member except the connection string, which is never logged. The
        ///     compiler's <c>ToString</c> calls it by its signature.
        /// </summary>
        /// <param name="builder">The builder the record's text is written to.</param>
        /// <returns>True, since members were written.</returns>
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "The record's synthesized ToString calls it.")]
        private bool PrintMembers(StringBuilder builder)
        {
            _ = builder
                .Append("Descriptor = ").Append(Descriptor)
                .Append(", Location = ").Append(Location)
                .Append(", ConnectionString = [redacted], Properties = ").Append(Properties)
                .Append(", Version = ").Append(Version);
            return true;
        }
    }
}
