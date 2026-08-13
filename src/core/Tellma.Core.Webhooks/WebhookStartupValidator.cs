// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Core.Webhooks
{
    /// <summary>
    ///     Validates the receiver set before the host serves traffic: keys unique and lowercase
    ///     kebab-case.
    /// </summary>
    /// <remarks>
    ///     Receiver keys are part of a connector's public surface — operators configure provider
    ///     dashboards against <c>/api/webhooks/{key}</c> — so a duplicate or malformed key is a
    ///     deployment-breaking mistake worth catching at startup rather than on the first callback.
    /// </remarks>
    /// <param name="scopeFactory">Creates the scope receivers are enumerated in, since a receiver
    ///     may legitimately be scoped.</param>
    internal sealed class WebhookStartupValidator(IServiceScopeFactory scopeFactory) : IHostedService
    {
        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = scopeFactory.CreateScope();

            List<string> failures = [];
            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (IWebhookReceiver receiver in scope.ServiceProvider.GetServices<IWebhookReceiver>())
            {
                string key = receiver.Key;
                if (!IsLowercaseKebabCase(key))
                {
                    failures.Add(
                        $"The webhook receiver {receiver.GetType().Name} declares the key '{key}', which is not lowercase kebab-case (letters, digits, and hyphens only).");
                    continue;
                }

                if (!seen.Add(key))
                {
                    failures.Add($"More than one webhook receiver declares the key '{key}'.");
                }
            }

            return failures.Count > 0
                ? throw new InvalidOperationException(
                    "The webhook composition is invalid: " + string.Join(" ", failures))
                : Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static bool IsLowercaseKebabCase(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (char c in value)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
