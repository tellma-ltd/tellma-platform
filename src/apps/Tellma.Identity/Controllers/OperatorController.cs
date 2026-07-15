// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Tellma.Identity.Controllers.Api;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Tap;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The operator (control-plane) surface: global user lookup, Temporary Access Pass
    ///     issuance, and audit access. Absent in in-proc deployments (no control plane). User
    ///     disable/enable/purge, impersonation, and scope/key administration are deferred.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="tapService">Temporary Access Pass issuance.</param>
    [ApiController]
    [ControlPlaneOnly]
    [Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
        Policy = ApiPolicies.ControlPlaneScope)]
    public sealed class OperatorController(
        UserManager<TellmaIdentityUser> userManager,
        ITemporaryAccessPassService tapService) : ControllerBase
    {
        /// <summary>Reads a global-directory user by subject.</summary>
        /// <param name="sub">The subject identifier.</param>
        /// <returns>The user, or 404.</returns>
        [HttpGet("api/identity/users/{sub}")]
        public async Task<ActionResult<OperatorUserResponse>> GetUser(string sub)
        {
            TellmaIdentityUser? user = await userManager.FindByIdAsync(sub);
            return user is null
                ? NotFound()
                : Ok(new OperatorUserResponse
                {
                    Sub = user.Id,
                    Email = user.Email,
                    DisplayName = user.DisplayName,
                    Locale = user.Locale,
                    LifecycleState = user.LifecycleState.ToString(),
                    CreatedUtc = user.CreatedUtc,
                });
        }

        /// <summary>Issues a Temporary Access Pass for admin-assisted recovery.</summary>
        /// <param name="sub">The subject to recover.</param>
        /// <returns>The one-time pass, or 404 when the user does not exist.</returns>
        [HttpPost("api/identity/users/{sub}/temporary-access-passes")]
        public async Task<ActionResult<TemporaryAccessPassResponse>> IssueTap(string sub)
        {
            IssuedTemporaryAccessPass? issued = await tapService.IssueAsync(
                sub, User.Identity?.Name, HttpContext.RequestAborted);

            return issued is null
                ? NotFound()
                : Ok(new TemporaryAccessPassResponse { Pass = issued.Pass, ExpiresUtc = issued.ExpiresUtc });
        }
    }
}
