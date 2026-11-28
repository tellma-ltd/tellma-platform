// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The catalog refused a write to a tenant's registration. Maps to 409 with the problem code
    ///     <see cref="TellmaException.Code" />: <c>tenant-registration-refused</c>, <c>tenant-illegal-transition</c>
    ///     or <c>tenant-location-refused</c>.
    /// </summary>
    /// <remarks>
    ///     A transition that loses a race to a concurrent one is refused like an illegal one, and a
    ///     relocation of a tenant whose state moved away from frozen or provisioning like one that
    ///     was never allowed: the catalog's own precondition decides, inside the write's transaction.
    /// </remarks>
    /// <param name="code">The refusal: one of the three codes above, from <see cref="TenantProblemCodes" />.</param>
    /// <param name="detail">A sentence for logs naming the tenant and the refused registration, transition or location.</param>
    /// <exception cref="ArgumentException"><paramref name="code" /> is not one of the codes above, or <paramref name="detail" /> is empty.</exception>
    public sealed class TenantStateException(string code, string detail)
        : TellmaException(ValidateCode(code), detail)
    {
        /// <summary>A sentence for logs naming the tenant and the refused registration, transition or location.</summary>
        public string Detail { get; } = detail;

        /// <summary>Refuses a code outside this exception's set.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The code.</returns>
        private static string ValidateCode(string code)
        {
            return code is TenantProblemCodes.RegistrationRefused
                or TenantProblemCodes.IllegalTransition
                or TenantProblemCodes.LocationRefused
                ? code
                : throw new ArgumentException($"'{code}' is not a code of a refused catalog write.", nameof(code));
        }
    }
}
