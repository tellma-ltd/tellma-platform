// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Re-raises a refusal OpenIddict already decided. The engine turns on error pass-through
    ///     for its interactive endpoints (authorization, end session, end-user verification), so a
    ///     request OpenIddict refused while validating it still reaches the endpoint's action, for
    ///     the error page to render. An action must therefore ask this first and return the
    ///     refusal it finds: a refused request never begins an interaction, signs anyone in or out,
    ///     or changes any state.
    /// </summary>
    public static class ProtocolRefusal
    {
        /// <summary>
        ///     The refusal of a request OpenIddict refused while validating it, as a result that
        ///     sends the same error through the protocol pipeline to the error page; null when the
        ///     request passed validation.
        /// </summary>
        /// <param name="context">The current request.</param>
        /// <returns>The refusal to return, or null.</returns>
        public static ForbidResult? Find(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context.GetOpenIddictServerResponse() is { Error: { Length: > 0 } error } response
                ? Forbid(error, response.ErrorDescription ?? string.Empty)
                : null;
        }

        /// <summary>
        ///     The refusal of an end-session request OpenIddict refused while validating it, as a
        ///     bare <c>400</c> that the error page renders from the refusal already on the
        ///     transaction; null when the request passed validation. The end-session endpoint takes
        ///     no challenge, so the refusal cannot be re-raised through the pipeline as
        ///     <see cref="Find" /> does.
        /// </summary>
        /// <param name="context">The current request.</param>
        /// <returns>The refusal to return, or null.</returns>
        public static StatusCodeResult? FindAtEndSession(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context.GetOpenIddictServerResponse() is { Error.Length: > 0 }
                ? new StatusCodeResult(StatusCodes.Status400BadRequest)
                : null;
        }

        /// <summary>A protocol error answered through the OpenIddict response pipeline.</summary>
        /// <param name="error">The OAuth error code.</param>
        /// <param name="description">The human-readable description.</param>
        /// <returns>The result.</returns>
        public static ForbidResult Forbid(string error, string description)
        {
            return new ForbidResult(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
                }));
        }
    }
}
