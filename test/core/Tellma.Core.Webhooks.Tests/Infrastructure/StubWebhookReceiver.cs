// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Core.Webhooks.Tests.Infrastructure
{
    /// <summary>A receiver that answers from a script and records what it was handed.</summary>
    /// <param name="key">The route segment it answers on.</param>
    /// <param name="handler">Produces the result; defaults to accepting.</param>
    public sealed class StubWebhookReceiver(
        string key, Func<WebhookRequest, WebhookResult>? handler = null) : IWebhookReceiver
    {
        /// <summary>Every request the fronting handed over.</summary>
        public List<WebhookRequest> Requests { get; } = [];

        /// <inheritdoc />
        public string Key => key;

        /// <inheritdoc />
        public Task<WebhookResult> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(handler?.Invoke(request) ?? new WebhookResult(WebhookOutcome.Accepted));
        }
    }
}
