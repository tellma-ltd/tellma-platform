// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    // The bootstrap is spec 0014 §11.
    /// <summary>
    ///     Makes a tenant's first individual administrator: a user with a permanent membership of the
    ///     administrator role. It runs in the platform's own scope of the tenant, which the security
    ///     guards do not hold back, so it can create the first administrator from none. One per scope.
    /// </summary>
    /// <remarks>
    ///     Idempotent on the email address: an existing user keeps its state and gains the membership
    ///     when it lacks one, and its id is returned again. An existing user who is deactivated is
    ///     refused rather than reactivated.
    /// </remarks>
    public interface ITenantBootstrapper
    {
        /// <summary>
        ///     Creates the user, or finds it by email, and gives it a permanent membership of the
        ///     administrator role, in one save through the users' stack. A subject, in a development
        ///     environment, is recorded on a user this call creates, as an invitation would record it.
        /// </summary>
        /// <param name="request">The administrator.</param>
        /// <param name="cancellationToken">Cancels the round trips.</param>
        /// <returns>The user's id.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        /// <exception cref="ArgumentException">The email address or the name is empty.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The scope is not the platform's own, the request carries a subject outside a
        ///     development environment, or the existing user of the email address is deactivated.
        /// </exception>
        Task<int> BootstrapAdministratorAsync(TenantBootstrapRequest request, CancellationToken cancellationToken = default);
    }
}
