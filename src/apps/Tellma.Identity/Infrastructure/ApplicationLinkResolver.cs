// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using System.Text.Json;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Invitations;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>Where the account pages send a user who wants to go back to the app.</summary>
    /// <param name="Url">The absolute URL to return to.</param>
    /// <param name="Name">The application's display name, for the link's label.</param>
    public sealed record ApplicationLink(string Url, string? Name);

    /// <summary>
    ///     Resolves the way back to the application a user came from.
    ///     <para>
    ///         The account pages are reached out of band, so nothing about the request says which
    ///         of a multi-distribution authority's applications the user belongs to. A caller may
    ///         say so with a <c>client_id</c>, which is resolved against the registered clients and
    ///         answered with that client's own registered origin. A caller may also name the page
    ///         it left, and that address is used only when it lies on that same registered origin —
    ///         so the link cannot be pointed anywhere the client is not already trusted to receive
    ///         users. A single-application deployment can instead configure one URL. With neither,
    ///         there is no link rather than a guess.
    ///     </para>
    /// </summary>
    /// <param name="applicationManager">The registered-client store.</param>
    /// <param name="options">The engine options, for the configured fallback.</param>
    public sealed class ApplicationLinkResolver(
        IOpenIddictApplicationManager applicationManager,
        IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>Resolves the back link for a request.</summary>
        /// <param name="clientId">The client the caller named, when it named one.</param>
        /// <param name="returnUrl">The address in that client's application the caller asked to return to.</param>
        /// <param name="cancellationToken">Aborts the lookup.</param>
        /// <returns>The link, or null when neither a client nor configuration supplies one.</returns>
        public async Task<ApplicationLink?> ResolveAsync(string? clientId, string? returnUrl, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(clientId)
                && await applicationManager.FindByClientIdAsync(clientId, cancellationToken) is { } application)
            {
                IReadOnlyDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);

                // First-party only: a third-party client is not somewhere this UI offers to send
                // a signed-in user, however legitimately it holds a registration.
                if (TellmaClientProperties.IsSet(properties, TellmaClientProperties.FirstParty)
                    && TellmaClientProperties.Get(properties, TellmaClientProperties.Origin) is { } origin)
                {
                    // The page the caller left, when it is on the client's own origin; otherwise
                    // the origin's root. The link is written from the parsed address rather than
                    // from the caller's text, so a browser cannot read it differently from the
                    // comparison that allowed it.
                    string url = !string.IsNullOrWhiteSpace(returnUrl)
                        && InvitationReturnUrlValidator.TryGetSameOriginUri(returnUrl, origin, out Uri? destination)
                            ? destination.AbsoluteUri
                            : origin;

                    return new ApplicationLink(
                        url, await applicationManager.GetLocalizedDisplayNameAsync(application, cancellationToken));
                }
            }

            // A third-party or unknown client, or none: the configured application, never an
            // address the caller supplied, because there is no registration to hold it against.
            return options.Value.Ui.ApplicationUrl is { } configured
                ? new ApplicationLink(configured, Name: null)
                : null;
        }
    }
}
