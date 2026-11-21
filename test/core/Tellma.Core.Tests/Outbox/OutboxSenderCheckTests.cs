// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Outbox;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The <c>outbox.sender</c> startup check: a host whose job worker runs and that registers no
    ///     email sender is a problem with a fix; a host with a sender, or whose worker is off, has none.
    /// </summary>
    public sealed class OutboxSenderCheckTests
    {
        [Fact]
        public async Task A_worker_without_a_sender_is_a_problem()
        {
            CompositionProblem problem = Assert.Single(await ProblemsAsync(jobs: true, sender: false));

            Assert.StartsWith("The job worker runs, but no email sender is registered", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("AddTellmaEmail", problem.Fix, StringComparison.Ordinal);
            Assert.Contains("Tellma:Jobs:Enabled = false", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_worker_with_a_sender_has_no_problem()
        {
            Assert.Empty(await ProblemsAsync(jobs: true, sender: true));
        }

        [Fact]
        public async Task A_host_whose_worker_is_off_needs_no_sender()
        {
            Assert.Empty(await ProblemsAsync(jobs: false, sender: false));
        }

        [Fact]
        public async Task The_check_is_registered_once_and_reads_the_deployment()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);

            OutboxSenderCheck check = Assert.Single(provider.GetServices<IStartupCheck>().OfType<OutboxSenderCheck>());
            Assert.Equal((CoreCheckNames.OutboxSender, true), (check.Name, check.RequiresInfrastructure));
        }

        /// <summary>Composes a host and runs every startup check, keeping the outbox sender check's problems.</summary>
        /// <param name="jobs">Whether the job worker runs.</param>
        /// <param name="sender">Whether an email sender is registered.</param>
        /// <returns>The check's problems.</returns>
        private static async Task<List<CompositionProblem>> ProblemsAsync(bool jobs, bool sender)
        {
            IServiceCollection services = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Jobs:Enabled", jobs ? "true" : "false"))));
            if (sender)
            {
                _ = services.AddSingleton<IEmailSender>(new NoSender());
            }

            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            IReadOnlyList<CompositionProblem> problems = await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: true, TestContext.Current.CancellationToken);
            return problems.From(CoreCheckNames.OutboxSender);
        }

        /// <summary>A sender the check only finds registered; it is never called.</summary>
        private sealed class NoSender : IEmailSender
        {
            /// <inheritdoc />
            public Task<IReadOnlyList<EmailSendResult>> SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The startup check never sends.");
            }
        }
    }
}
