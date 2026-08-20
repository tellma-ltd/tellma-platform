// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using System.Text.Json;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Services.Invitations
{
    /// <summary>
    ///     Decides where an accepted invitation may send the user.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An invitation is the one place the authority sends someone onward to a tenant, so it
    ///         is the one place an absolute destination is allowed at all. Everywhere else
    ///         <see cref="ReturnUrlValidator" /> still permits local paths only, and that stays
    ///         true: this does not loosen the rule, it adds a second, narrower one beside it.
    ///     </para>
    ///     <para>
    ///         The narrowing is that an absolute destination must be the calling client's own
    ///         registered origin. Nothing here is taken on the caller's word — the origin is read
    ///         from the registration, so the set of places an invitation can land is fixed when the
    ///         distribution is provisioned, not when the invite is made. A client that has not
    ///         registered an origin cannot name one.
    ///     </para>
    /// </remarks>
    /// <param name="applicationManager">The registered-client store.</param>
    public sealed class InvitationReturnUrlValidator(IOpenIddictApplicationManager applicationManager)
    {
        /// <summary>Whether a return url may be stored against an invitation this client raised.</summary>
        /// <param name="returnUrl">The destination the caller asked for; null is always allowed.</param>
        /// <param name="clientId">The client raising the invitation.</param>
        /// <param name="cancellationToken">Aborts the lookup.</param>
        /// <returns>True when the destination is one this client may send a user to.</returns>
        public async Task<bool> IsAllowedAsync(
            string? returnUrl, string? clientId, CancellationToken cancellationToken)
        {
            // Nothing asked for, or a local path — the latter being what this field has always
            // meant, and it goes nowhere off-origin.
            return string.IsNullOrWhiteSpace(returnUrl)
                || ReturnUrlValidator.IsValid(returnUrl)
                || (await ResolveOriginAsync(clientId, cancellationToken) is { } origin
                    && IsSameOrigin(returnUrl, origin));
        }

        /// <summary>
        ///     Whether an absolute url belongs to an origin. Compared as parsed URIs rather than as
        ///     text: a prefix match would accept <c>https://acme.example.com.evil.test</c> for
        ///     <c>https://acme.example.com</c>, and a host comparison alone would accept the same
        ///     host over plain HTTP or on another port.
        /// </summary>
        /// <param name="returnUrl">The candidate destination.</param>
        /// <param name="origin">The client's registered origin.</param>
        /// <returns>True when the two share scheme, host and port.</returns>
        internal static bool IsSameOrigin(string returnUrl, string origin)
        {
            if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out Uri? candidate)
                || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? registered))
            {
                return false;
            }

            // Only the web schemes, so a registration can never be talked into authorizing a
            // destination the browser would hand to something other than a page.
            return (candidate.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps)
                && string.Equals(candidate.Scheme, registered.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Host, registered.Host, StringComparison.OrdinalIgnoreCase)
                && candidate.Port == registered.Port

                // A userinfo section is how "https://acme.example.com@evil.test" is made to read
                // like the registered origin at a glance. Nothing legitimate needs one.
                && string.IsNullOrEmpty(candidate.UserInfo);
        }

        /// <summary>The origin a client is registered to receive users at, when it has one.</summary>
        private async Task<string?> ResolveOriginAsync(string? clientId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(clientId)
                || await applicationManager.FindByClientIdAsync(clientId, cancellationToken) is not { } application)
            {
                return null;
            }

            IReadOnlyDictionary<string, JsonElement> properties =
                await applicationManager.GetPropertiesAsync(application, cancellationToken);

            // First-party only, on the same reasoning the account pages' back link uses: a
            // third-party client is not somewhere this authority delivers a freshly created user.
            return TellmaClientProperties.IsSet(properties, TellmaClientProperties.FirstParty)
                ? TellmaClientProperties.Get(properties, TellmaClientProperties.Origin)
                : null;
        }
    }
}
