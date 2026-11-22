// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Jobs;

namespace Tellma.Core.Outbox
{
    /// <summary>
    ///     <c>outbox.sender</c>: a host whose job worker runs registers an email sender, so the
    ///     outgoing emails' sending jobs can send. A host whose worker is configured off sends nothing
    ///     and needs none.
    /// </summary>
    /// <remarks>
    ///     The check reads the deployment's configuration and registrations, so a build step that
    ///     realises the composition without a deployment skips it. In Development the email
    ///     pipeline's log sink is a sender.
    /// </remarks>
    /// <param name="record">The realised composition, whose configuration says whether the worker runs.</param>
    internal sealed class OutboxSenderCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.OutboxSender;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- A worker that runs the sending jobs needs a sender to hand their emails to ---
            bool missing = JobsComposition.Enabled(record.Configuration)
                && services.GetService<IServiceProviderIsService>() is IServiceProviderIsService registered
                && !registered.IsService(typeof(IEmailSender));
            IReadOnlyList<CompositionProblem> problems = missing
                ?
                [
                    new CompositionProblem(
                        Name,
                        "The job worker runs, but no email sender is registered, so the outgoing emails' sending jobs cannot run and no email is ever sent.",
                        "Compose the email pipeline (AddTellmaEmail) with a transport in the host, or turn the worker off with Tellma:Jobs:Enabled = false on a host that runs no jobs."),
                ]
                : [];
            return Task.FromResult(problems);
        }
    }
}
