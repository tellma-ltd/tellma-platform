// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Abstractions.Identity
{
    /// <summary>One user to invite through <see cref="IIdentityServerClient.InviteAsync" />.</summary>
    /// <param name="Email">The address the identity user is created or found by, and the invitation sent to.</param>
    /// <param name="DisplayName">The name the invitation greets the user by; null for none.</param>
    /// <param name="Locale">The language the invitation is written in, such as <c>ar-SA</c>; null for the server's default.</param>
    /// <param name="Gender">The grammatical gender the invitation addresses the user in; null for neutral forms.</param>
    /// <param name="ReturnUrl">Where an accepted invitation sends the user; null for the server's own landing page.</param>
    /// <param name="ExistingOnly">
    ///     True to only look the address up: the server never creates a user and never sends mail. An
    ///     existing user who holds a credential answers <see cref="InviteStatus.Active" /> with their
    ///     subject; an existing user without one answers the per-user error
    ///     <c>existing_user_has_no_credential</c>; an unknown address answers <c>unknown_email</c>.
    ///     Sandbox tenants set it, so they never cause the identity server to email anyone.
    /// </param>
    public sealed record IdentityInvitation(
        string Email,
        string? DisplayName,
        string? Locale,
        Gender? Gender,
        string? ReturnUrl,
        bool ExistingOnly = false);
}
