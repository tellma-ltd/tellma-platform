// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using System.Text.Json;
using Tellma.Identity.Options;
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
    ///         answered with that client's own registered origin — never with a URL the caller
    ///         supplied, so the link cannot be pointed anywhere the client is not already trusted
    ///         to receive users. A single-application deployment can instead configure one URL.
    ///         With neither, there is no link rather than a guess.
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
        /// <param name="cancellationToken">Aborts the lookup.</param>
        /// <returns>The link, or null when neither a client nor configuration supplies one.</returns>
        public async Task<ApplicationLink?> ResolveAsync(string? clientId, CancellationToken cancellationToken)
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
                    return new ApplicationLink(
                        origin, await applicationManager.GetLocalizedDisplayNameAsync(application, cancellationToken));
                }
            }

            return options.Value.Ui.ApplicationUrl is { } configured
                ? new ApplicationLink(configured, Name: null)
                : null;
        }
    }
}
