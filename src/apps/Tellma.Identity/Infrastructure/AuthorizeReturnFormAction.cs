// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using Tellma.Identity.Options;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Lets a sign-in page finish the authorization request it is standing in front of.
    ///     <para>
    ///         Every interactive page in the flow completes through a form, and the redirect that
    ///         form is answered with runs back to the authorization endpoint and on to the client's
    ///         callback — another origin. A browser applies <c>form-action</c> to every hop of the
    ///         navigation a submission produces, judged against the policy of the document holding
    ///         the form, so a page that does not name that callback watches the user sign in
    ///         successfully and go nowhere: the code is issued, the session is stamped, nothing is
    ///         logged as an error, and the page simply does not move. Same mechanism as the consent
    ///         screen's "Allow" button and the external-provider buttons.
    ///     </para>
    ///     <para>
    ///         Only the client's <em>registered</em> callbacks are named, and only when the return
    ///         URL is this engine's own authorization endpoint — so the policy widens by exactly
    ///         where the request could always have landed, and nothing off the URL itself is
    ///         trusted. A crafted return URL naming someone else's client buys nothing: it widens
    ///         to that client's legitimate callbacks, which is where an ordinary authorization for
    ///         it would go anyway.
    ///     </para>
    /// </summary>
    /// <param name="applicationManager">The client registry.</param>
    /// <param name="engineOptions">The engine options (the mount path base).</param>
    public sealed class AuthorizeReturnFormAction(
        IOpenIddictApplicationManager applicationManager,
        IOptions<TellmaIdentityOptions> engineOptions)
    {
        /// <summary>The engine-relative path of the authorization endpoint.</summary>
        private const string AuthorizePath = "/connect/authorize";

        /// <summary>
        ///     Widens this response's <c>form-action</c> policy to the callbacks of the client
        ///     whose authorization request the given destination resumes. A destination that is
        ///     not one — an ordinary local page, or nothing at all — changes no policy.
        /// </summary>
        /// <param name="context">The request whose response is being built.</param>
        /// <param name="returnUrl">Where the page's form submission will end up going.</param>
        /// <param name="cancellationToken">Aborts the lookup.</param>
        /// <returns>A task that completes once the policy has been decided.</returns>
        public async Task AllowAsync(HttpContext context, string? returnUrl, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (ClientIdOf(returnUrl) is not { } clientId)
            {
                return;
            }

            // An unregistered client id names no callbacks, so it widens nothing — the same answer
            // the authorization endpoint gives such a request.
            if (await applicationManager.FindByClientIdAsync(clientId, cancellationToken) is not { } application)
            {
                return;
            }

            CspFormAction.Allow(
                context, await applicationManager.GetRedirectUrisAsync(application, cancellationToken));
        }

        /// <summary>
        ///     Reads the client id out of a return URL, when that URL is an authorization request
        ///     on this engine.
        /// </summary>
        /// <param name="returnUrl">The candidate destination.</param>
        /// <returns>The client id, or null when the destination is not an authorization request.</returns>
        private string? ClientIdOf(string? returnUrl)
        {
            // Local URLs only, so a destination that merely looks like the authorization endpoint
            // on some other host cannot reach the registry at all.
            if (!ReturnUrlValidator.IsValid(returnUrl))
            {
                return null;
            }

            int mark = returnUrl!.IndexOf('?', StringComparison.Ordinal);
            if (mark < 0)
            {
                return null;
            }

            // The path is matched whole against the mounted endpoint rather than by suffix, so a
            // page under some other route cannot borrow the widening by ending in the same text.
            string path = returnUrl[..mark];
            if (!string.Equals(path, engineOptions.Value.PathBase + AuthorizePath, StringComparison.Ordinal))
            {
                return null;
            }

            QueryHelpers.ParseQuery(returnUrl[mark..])
                .TryGetValue(OpenIddictConstants.Parameters.ClientId, out StringValues found);

            string clientId = found.ToString();
            return string.IsNullOrWhiteSpace(clientId) ? null : clientId;
        }
    }
}
