// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Lets a page that offers external sign-in buttons reach the providers those buttons name.
    ///     <para>
    ///         The buttons are forms, and starting one is answered with a redirect to the
    ///         provider's authorization endpoint. A browser applies <c>form-action</c> to every hop
    ///         of a submission's navigation, and the policy governing that is the one on the
    ///         document holding the form — so a page that renders a provider button and does not
    ///         name the provider watches the button do nothing while the server issues a perfectly
    ///         good redirect. Same mechanism as the consent screen's "Allow".
    ///     </para>
    ///     <para>
    ///         Origins, not endpoint URLs: because the rule follows the whole redirect chain, a
    ///         provider that bounces once inside its own site before rendering — which both of
    ///         these do — would be cut off by a source pinned to the exact authorization path.
    ///     </para>
    /// </summary>
    public static class ExternalProviderFormAction
    {
        /// <summary>Names the given providers as permissible form targets for this response.</summary>
        /// <param name="context">The request whose response is being built.</param>
        /// <param name="providers">The providers whose buttons this page renders.</param>
        public static void Allow(HttpContext context, IEnumerable<string> providers)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(providers);

            CspFormAction.Allow(
                context,
                providers.Select(provider => OriginOf(context, provider)).OfType<string>());
        }

        /// <summary>
        ///     The origin of a provider's configured authorization endpoint, or null for a provider
        ///     the engine does not register. Read from the handler's own options rather than a
        ///     constant, so a deployment that repoints a provider — a sovereign cloud, say — widens
        ///     the policy to wherever it actually sends people.
        /// </summary>
        private static string? OriginOf(HttpContext context, string provider)
        {
            string? endpoint = null;
            if (string.Equals(provider, GoogleDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = context.RequestServices.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
                    .Get(GoogleDefaults.AuthenticationScheme).AuthorizationEndpoint;
            }
            else if (string.Equals(provider, MicrosoftAccountDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = context.RequestServices.GetRequiredService<IOptionsMonitor<MicrosoftAccountOptions>>()
                    .Get(MicrosoftAccountDefaults.AuthenticationScheme).AuthorizationEndpoint;
            }

            return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed)
                ? parsed.GetLeftPart(UriPartial.Authority)
                : null;
        }
    }
}
