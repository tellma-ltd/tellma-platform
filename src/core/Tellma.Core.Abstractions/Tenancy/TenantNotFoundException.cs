// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The tenant does not exist for this caller: absent from the catalog, provisioning or
    ///     retired, or the caller is not one of its active members. Maps to 404 with the problem code
    ///     <c>tenant-not-found</c>.
    /// </summary>
    /// <remarks>
    ///     Every cause answers alike, so a request cannot learn whether a tenant it may not enter
    ///     exists. The exception carries no display arguments for the same reason.
    /// </remarks>
    /// <param name="tenantId">The tenant the request named.</param>
    public sealed class TenantNotFoundException(int tenantId)
        : TellmaException(
            TenantProblemCodes.NotFound,
            string.Create(CultureInfo.InvariantCulture, $"Tenant {tenantId} was not found."))
    {
        /// <summary>The tenant the request named.</summary>
        public int TenantId { get; } = tenantId;
    }
}
