// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using System.Diagnostics;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The page every failure that cannot be answered any other way ends on. Three kinds arrive
    ///     here: an OpenID Connect error with nowhere to redirect to (an unknown client, a bad
    ///     <c>redirect_uri</c>, an unsatisfiable request), a bare status code from a request that
    ///     matched nothing, and — where the host wires an exception handler to this path — the
    ///     server's own unhandled faults.
    ///     <para>
    ///         What it shows differs accordingly. A protocol error carries a code and a description
    ///         written to be read by the person who caused it, and those are surfaced. A server
    ///         fault says nothing about itself, because nothing it could say is both safe and
    ///         useful. Every one of them carries the trace identifier, which is the only thing that
    ///         lets a user quote a failure and an operator find it: it is the id the logs and any
    ///         collector already correlate on, so it needs no recording here to be searchable.
    ///     </para>
    ///     <para>Raw request data is never echoed.</para>
    /// </summary>
    public sealed class ErrorController : Controller
    {
        /// <summary>Renders the error page.</summary>
        /// <returns>The error view.</returns>
        [Route("error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            // Present only when an exception handler re-executed the request through here, which is
            // what distinguishes the server's fault from the caller's.
            bool serverFault = HttpContext.Features.Get<IExceptionHandlerFeature>() is not null;

            // When the failure was an OpenID Connect one, surface its sanitized details.
            OpenIddictResponse? response = HttpContext.GetOpenIddictServerResponse();

            return View("Error", new ViewModels.ErrorViewModel
            {
                Error = serverFault ? null : response?.Error,
                ErrorDescription = serverFault ? null : response?.ErrorDescription,

                // The distributed-trace id where one exists, so the reference the user reads off
                // the screen is the same one a collector indexed the request under; the connection
                // -scoped identifier otherwise, which is what the logs fall back to as well.
                Reference = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                IsServerFault = serverFault,
            });
        }
    }
}
