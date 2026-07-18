// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using Tellma.Identity.Controllers.Api;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The distribution-facing service-account API (machine-to-machine, <c>tellma_identity</c>
    ///     scope). Creating an account returns its secret exactly once; the lost-secret path is
    ///     delete and recreate.
    /// </summary>
    /// <param name="provisioning">The client-provisioning service.</param>
    [ApiController]
    [Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = ApiPolicies.IdentityScope)]
    public sealed class ServiceAccountsController(IClientProvisioningService provisioning) : ControllerBase
    {
        /// <summary>Creates a service account and returns its credentials once.</summary>
        /// <param name="request">The account details.</param>
        /// <returns>The created account's client id and one-time secret.</returns>
        [HttpPost("api/identity/service-accounts")]
        public async Task<ActionResult<CreateServiceAccountResponse>> Create([FromBody] CreateServiceAccountRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            ServiceAccountCredentials credentials;
            try
            {
                credentials = await provisioning.CreateServiceAccountAsync(
                    request.DisplayName, [.. request.Resources], CallerClientId(), HttpContext.RequestAborted);
            }
            catch (ProvisioningValidationException exception)
            {
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            return CreatedAtAction(nameof(Get), new { clientId = credentials.ClientId }, new CreateServiceAccountResponse
            {
                ClientId = credentials.ClientId,
                ClientSecret = credentials.ClientSecret,
            });
        }

        /// <summary>Reads a service account's metadata (never the secret).</summary>
        /// <param name="clientId">The service-account client id.</param>
        /// <returns>The metadata, or 404.</returns>
        [HttpGet("api/identity/service-accounts/{clientId}")]
        public async Task<ActionResult<ServiceAccountResponse>> Get(string clientId)
        {
            ServiceAccountDetails? details = await provisioning.GetServiceAccountAsync(clientId, HttpContext.RequestAborted);
            return details is null
                ? NotFound()
                : Ok(new ServiceAccountResponse
                {
                    ClientId = details.ClientId,
                    DisplayName = details.DisplayName,
                    CreatedUtc = details.CreatedUtc,
                });
        }

        /// <summary>Deletes a service account.</summary>
        /// <param name="clientId">The service-account client id.</param>
        /// <returns>204 on success, 404 when it does not exist.</returns>
        [HttpDelete("api/identity/service-accounts/{clientId}")]
        public async Task<IActionResult> Delete(string clientId)
        {
            bool deleted = await provisioning.DeleteServiceAccountAsync(clientId, CallerClientId(), HttpContext.RequestAborted);
            return deleted ? NoContent() : NotFound();
        }

        /// <summary>The calling client's id, for audit attribution.</summary>
        private string? CallerClientId()
        {
            return User.GetClaim(OpenIddictConstants.Claims.ClientId) ?? User.GetClaim(OpenIddictConstants.Claims.Subject);
        }
    }
}
