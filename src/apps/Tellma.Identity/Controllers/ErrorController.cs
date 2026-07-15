// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     Renders protocol errors that cannot be redirected to a client (OpenIddict error
    ///     pass-through re-executed through the status-code pages middleware). Never echoes raw
    ///     request data.
    /// </summary>
    public sealed class ErrorController : Controller
    {
        /// <summary>Renders the error page.</summary>
        /// <returns>The error view.</returns>
        [Route("error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // When the failure was an OpenID Connect one, surface its sanitized details.
            OpenIddictResponse? response = HttpContext.GetOpenIddictServerResponse();

            return View("Error", new ViewModels.ErrorViewModel
            {
                Error = response?.Error,
                ErrorDescription = response?.ErrorDescription,
            });
        }
    }
}
