// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using Tellma.Identity.Controllers.Api;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Invitations;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The distribution-facing bulk-invitation API (machine-to-machine, <c>tellma_identity</c>
    ///     scope). The whole batch is one operation returning per-user status and <c>sub</c> — a
    ///     refused user carries a per-user error while the rest of the batch proceeds; the
    ///     invitation link is never in the response, in any environment.
    /// </summary>
    /// <param name="invitationService">The bulk invitation service.</param>
    /// <param name="deliveryStatusService">Reads what became of invitations already raised.</param>
    [ApiController]
    [Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = ApiPolicies.IdentityScope)]
    public sealed class InvitationsController(
        InvitationService invitationService,
        InvitationDeliveryStatusService deliveryStatusService) : ControllerBase
    {
        /// <summary>Invites a batch of users.</summary>
        /// <param name="request">The users to invite.</param>
        /// <returns>The per-user results.</returns>
        [HttpPost("api/identity/invitations")]
        public async Task<ActionResult<InviteUsersResponse>> Invite([FromBody] InviteUsersRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            IReadOnlyList<InvitationRequestItem> items =
                [.. request.Users.Select(static user => new InvitationRequestItem(
                    user.Email,
                    user.DisplayName,
                    user.Locale,
                    user.ReturnUrl,
                    // An unrecognized value is treated as unstated rather than refused: gender is
                    // optional, and a caller sending something we do not model should get a user
                    // addressed neutrally, not a failed invitation.
                    Enum.TryParse(user.Gender, ignoreCase: true, out UserGender parsed) ? parsed : null))];

            string? clientId = User.GetClaim(OpenIddictConstants.Claims.ClientId)
                ?? User.GetClaim(OpenIddictConstants.Claims.Subject);

            IReadOnlyList<InvitationResultItem> results =
                await invitationService.InviteAsync(items, clientId, HttpContext.RequestAborted);

            return Ok(new InviteUsersResponse
            {
                Results = [.. results.Select(static result => new InviteUserResult
                {
                    Email = result.Email,
                    Sub = result.Subject,
                    Status = result.Status?.ToString(),
                    Error = result.Error,
                })],
            });
        }

        /// <summary>Reads what became of the invitations this caller raised.</summary>
        /// <param name="request">The subjects to report on.</param>
        /// <returns>One result per requested subject, in request order.</returns>
        /// <remarks>
        ///     Bulk-shaped for the same reason the invite is: a distribution's admin screen asks
        ///     about a page of users at once, and a per-user endpoint would turn that into a page
        ///     of round trips.
        /// </remarks>
        [HttpPost("api/identity/invitations/delivery-status")]
        public async Task<ActionResult<InvitationDeliveryStatusResponse>> DeliveryStatus(
            [FromBody] InvitationDeliveryStatusRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Resolved exactly as the invite does, because it has to match what was recorded then.
            string? clientId = User.GetClaim(OpenIddictConstants.Claims.ClientId)
                ?? User.GetClaim(OpenIddictConstants.Claims.Subject);

            // A token carrying the scope but naming no client would otherwise be scoped to rows
            // whose creating client is also null, which is every invitation raised without one.
            // There is no caller this could legitimately be, so it is refused rather than narrowed.
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return Forbid();
            }

            IReadOnlyList<InvitationDeliveryStatus> statuses = await deliveryStatusService.ReadAsync(
                [.. request.Subs], clientId, HttpContext.RequestAborted);

            return Ok(new InvitationDeliveryStatusResponse
            {
                Results = [.. statuses.Select(static status => new InvitationDeliveryStatusResult
                {
                    Sub = status.Subject,
                    State = status.State.ToString(),
                    ExpectsDeliveryEvents = status.ExpectsDeliveryEvents,
                    SentUtc = status.SentUtc,
                    UpdatedUtc = status.UpdatedUtc,
                    Reason = status.Reason,
                })],
            });
        }
    }
}
