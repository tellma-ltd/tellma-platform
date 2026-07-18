// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Tap
{
    /// <summary>An issued Temporary Access Pass; the clear pass is returned only once.</summary>
    /// <param name="Pass">The clear pass value, shown to the operator once.</param>
    /// <param name="ExpiresUtc">When the pass expires (at most one hour out).</param>
    public sealed record IssuedTemporaryAccessPass(string Pass, DateTimeOffset ExpiresUtc);

    /// <summary>
    ///     Admin-assisted recovery via a Temporary Access Pass: an operator issues a short-lived,
    ///     single-use pass conveyed out-of-band; the user redeems it once for a limited session
    ///     whose only exit is enrolling a new credential.
    /// </summary>
    public interface ITemporaryAccessPassService
    {
        /// <summary>Issues a pass for a user, superseding any outstanding one.</summary>
        /// <param name="userId">The user to recover.</param>
        /// <param name="issuedByClientId">The operator client, for audit.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The issued pass, or null when the user does not exist.</returns>
        Task<IssuedTemporaryAccessPass?> IssueAsync(string userId, string? issuedByClientId, CancellationToken cancellationToken);

        /// <summary>Redeems a pass, consuming it, and returns the user it recovers.</summary>
        /// <param name="email">The email identifying the account.</param>
        /// <param name="pass">The clear pass value.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The recovered user id on success, or null.</returns>
        Task<string?> RedeemAsync(string email, string pass, CancellationToken cancellationToken);
    }
}
