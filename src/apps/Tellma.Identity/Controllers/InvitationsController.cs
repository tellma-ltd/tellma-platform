// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using Tellma.Identity.Controllers.Api;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Invitations;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The distribution-facing bulk-invitation API (machine-to-machine, <c>tellma_identity</c>
    ///     scope). The whole batch is one operation returning per-user status and <c>sub</c>; the
    ///     invitation link is never in the response, in any environment.
    /// </summary>
    /// <param name="invitationService">The bulk invitation service.</param>
    [ApiController]
    [Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = ApiPolicies.IdentityScope)]
    public sealed class InvitationsController(InvitationService invitationService) : ControllerBase
    {
        /// <summary>Invites a batch of users.</summary>
        /// <param name="request">The users to invite.</param>
        /// <returns>The per-user results.</returns>
        [HttpPost("api/identity/invitations")]
        public async Task<ActionResult<InviteUsersResponse>> Invite([FromBody] InviteUsersRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            IReadOnlyList<InvitationRequestItem> items =
                [.. request.Users.Select(static user => new InvitationRequestItem(user.Email, user.DisplayName, user.Locale, user.ReturnUrl))];

            string? clientId = User.GetClaim(OpenIddictConstants.Claims.ClientId)
                ?? User.GetClaim(OpenIddictConstants.Claims.Subject);

            IReadOnlyList<InvitationResultItem> results;
            try
            {
                results = await invitationService.InviteAsync(items, clientId, HttpContext.RequestAborted);
            }
            catch (Services.Provisioning.ProvisioningValidationException exception)
            {
                return Problem(detail: exception.Message, statusCode: Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest);
            }

            return Ok(new InviteUsersResponse
            {
                Results = [.. results.Select(static result => new InviteUserResult
                {
                    Email = result.Email,
                    Sub = result.Subject,
                    Status = result.Status.ToString(),
                })],
            });
        }
    }
}
