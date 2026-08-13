// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Webhooks
{
    /// <summary>
    ///     A connector adapter's inbound half: verifies, translates, and forwards callbacks from one
    ///     external system. Implementations are transport-neutral; a host fronts them with HTTP
    ///     (the platform projects <c>/api/webhooks/{Key}</c>; a minimal host maps the route by hand).
    /// </summary>
    /// <remarks>
    ///     Implementations must verify the payload signature over the raw <see cref="WebhookRequest.Body" />
    ///     bytes before trusting any content, and must return promptly: verify, translate, hand off to
    ///     a dispatcher — never process inline.
    /// </remarks>
    public interface IWebhookReceiver
    {
        /// <summary>
        ///     The route segment identifying this receiver (lowercase kebab-case, e.g.
        ///     "sendgrid-events"). Uniqueness across the composition is validated at startup.
        /// </summary>
        string Key { get; }

        /// <summary>Handles one webhook call.</summary>
        /// <param name="request">The raw inbound request.</param>
        /// <param name="cancellationToken">Abandons the handling.</param>
        /// <returns>The semantic outcome, which the host maps to a status code.</returns>
        Task<WebhookResult> HandleAsync(WebhookRequest request, CancellationToken cancellationToken);
    }
}
