// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     The second half of the email startup gate, running after options validation and before the
    ///     host serves traffic: it warms the active transport so that adapter's own configuration is
    ///     checked now rather than on the first send, validates delivery-event owner keys, and logs
    ///     what the deployment ended up with.
    /// </summary>
    /// <remarks>
    ///     Only the <em>active</em> transport's factories are invoked. Referencing an adapter without
    ///     configuring it is legal as long as it is not the active provider, which is what makes a
    ///     distribution able to compile in every transport and choose one per deployment. The
    ///     corollary is binding on adapter authors: bind options and register an
    ///     <c>IValidateOptions&lt;T&gt;</c>, but never call <c>ValidateOnStart</c>, or an inactive
    ///     adapter would fail startup for configuration nobody asked it to have.
    /// </remarks>
    /// <param name="selector">The resolved active transport.</param>
    /// <param name="scopeFactory">Creates the scope the transport factories are warmed in.</param>
    /// <param name="deployment">This deployment's identity, for the startup log line.</param>
    /// <param name="logger">Where the startup line goes.</param>
    internal sealed class EmailStartupValidator(
        EmailTransportSelector selector,
        IServiceScopeFactory scopeFactory,
        DeploymentIdentity deployment,
        ILogger<EmailStartupValidator> logger) : IHostedService
    {
        /// <summary>
        ///     The suffix by which a transport's delivery-event receiver is recognized: the SendGrid
        ///     transport's receiver is "sendgrid-events", the ACS one's is "acs-email-events".
        /// </summary>
        internal const string DeliveryReceiverKeySuffix = "-events";

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            EmailTransportRegistration active = selector.Active;

            // A scope, not the root provider: an adapter's factory may resolve scoped services, and
            // warming it here is exactly what makes its own options validation fire at startup.
            using IServiceScope scope = scopeFactory.CreateScope();
            _ = active.Live(scope.ServiceProvider);
            _ = active.Sandbox?.Invoke(scope.ServiceProvider);

            ValidateOwnerKeys(scope.ServiceProvider);

            // Receivers are resolved from the scope rather than injected: they are registered scoped
            // because they depend on the scoped dispatcher, and taking them on a singleton hosted
            // service's constructor would fail scope validation in Development and capture a scoped
            // dispatcher for the life of the process everywhere else.
            string expectedReceiverKey = active.Name + DeliveryReceiverKeySuffix;
            bool webhookConfigured = scope.ServiceProvider
                .GetServices<IWebhookReceiver>()
                .Any(r => string.Equals(r.Key, expectedReceiverKey, StringComparison.Ordinal));

            EmailLog.ActiveTransportSelected(
                logger, active.Name, deployment.DeploymentId, webhookConfigured, active.Sandbox is not null);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static void ValidateOwnerKeys(IServiceProvider services)
        {
            List<string> failures = [];
            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (IEmailDeliveryEventHandler handler in services.GetServices<IEmailDeliveryEventHandler>())
            {
                string ownerKey = handler.OwnerKey;
                if (string.IsNullOrEmpty(ownerKey) || !EmailCorrelation.IsValidOwnerKey(ownerKey))
                {
                    failures.Add(
                        $"The delivery-event handler {handler.GetType().Name} declares the owner key '{ownerKey}', which is not lowercase kebab-case.");
                    continue;
                }

                if (!seen.Add(ownerKey))
                {
                    failures.Add($"More than one delivery-event handler declares the owner key '{ownerKey}'.");
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "The email delivery-event composition is invalid: " + string.Join(" ", failures));
            }
        }
    }
}
