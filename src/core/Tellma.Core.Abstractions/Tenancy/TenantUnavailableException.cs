// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The tenant exists but cannot serve this request or this background work now. The code says
    ///     why: <c>catalog-unavailable</c> and <c>tenant-schema-behind</c> map to 503 with a
    ///     <c>Retry-After</c> header; <c>tenant-suspended</c>, <c>tenant-read-only</c> and
    ///     <c>tenant-frozen</c> map to 403; <c>tenant-not-found</c> maps to 404.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>catalog-unavailable</c> means the process's tenant snapshot is older than its
    ///         staleness bound, or was never loaded, so no tenant is served until the catalog answers
    ///         again. <c>tenant-schema-behind</c> means the tenant database's recorded schema does not
    ///         include the running model's.
    ///     </para>
    ///     <para>
    ///         <c>tenant-not-found</c> is the code of a background scope refused for a provisioning or
    ///         a retired tenant. A request sees such a tenant as absent
    ///         (<see cref="TenantNotFoundException" />), and the code keeps it so should the refusal
    ///         ever reach a response, while <see cref="State" /> tells the background caller the
    ///         state that refused it.
    ///     </para>
    /// </remarks>
    /// <param name="tenantId">The tenant the request named.</param>
    /// <param name="state">The tenant's state when a state refused the request; null for the catalog and schema codes.</param>
    /// <param name="code">One of the codes above, from <see cref="TenantProblemCodes" />.</param>
    /// <param name="retryAfter">How long the caller should wait before retrying; null when retrying will not help.</param>
    /// <exception cref="ArgumentException"><paramref name="code" /> is not one of the codes above.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryAfter" /> is negative.</exception>
    public sealed class TenantUnavailableException(int tenantId, TenantState? state, string code, TimeSpan? retryAfter)
        : TellmaException(
            ValidateCode(code),
            string.Create(CultureInfo.InvariantCulture, $"Tenant {tenantId} cannot serve the request: {code}."))
    {
        /// <summary>The tenant the request named.</summary>
        public int TenantId { get; } = tenantId;

        /// <summary>The tenant's state when a state refused the request; null for the catalog and schema codes.</summary>
        public TenantState? State { get; } = state;

        /// <summary>How long the caller should wait before retrying; null when retrying will not help.</summary>
        public TimeSpan? RetryAfter { get; } = ValidateRetryAfter(retryAfter);

        /// <summary>Refuses a code outside this exception's set.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The code.</returns>
        private static string ValidateCode(string code)
        {
            return code is TenantProblemCodes.NotFound
                or TenantProblemCodes.CatalogUnavailable
                or TenantProblemCodes.SchemaBehind
                or TenantProblemCodes.Suspended
                or TenantProblemCodes.ReadOnly
                or TenantProblemCodes.Frozen
                ? code
                : throw new ArgumentException($"'{code}' is not a code of an unavailable tenant.", nameof(code));
        }

        /// <summary>Refuses a negative wait.</summary>
        /// <param name="retryAfter">The wait.</param>
        /// <returns>The wait.</returns>
        private static TimeSpan? ValidateRetryAfter(TimeSpan? retryAfter)
        {
            return retryAfter is not { } value || value >= TimeSpan.Zero
                ? retryAfter
                : throw new ArgumentOutOfRangeException(nameof(retryAfter), retryAfter, "A retry-after wait is zero or positive.");
        }
    }
}
