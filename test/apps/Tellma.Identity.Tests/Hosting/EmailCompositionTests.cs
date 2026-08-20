// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Services.Email;

namespace Tellma.Identity.Tests.Hosting
{
    /// <summary>
    ///     The engine sends mail through a contract it does not implement, from a background worker
    ///     whose failures are caught and logged. A host that forgets to compose the email pipeline
    ///     therefore has nothing to fail on: it boots, it serves sign-in, and every code and
    ///     invitation link goes into a warning. These pin the guard that turns that into a refusal
    ///     to start.
    /// </summary>
    public sealed class EmailCompositionTests
    {
        [Fact]
        public async Task A_host_that_composed_no_transport_refuses_to_start()
        {
            using ServiceProvider provider = Compose(withSender: false);

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => WorkerFor(provider).StartingAsync(TestContext.Current.CancellationToken));

            // The message has to name the fix, because the person who sees it is composing a host
            // and the symptom otherwise arrives much later, as mail that never came.
            Assert.Contains("AddTellmaEmail", failure.Message, StringComparison.Ordinal);
            Assert.Contains("silently discarded", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_host_that_composed_a_transport_starts()
        {
            using ServiceProvider provider = Compose(withSender: true);

            await WorkerFor(provider).StartingAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Builds the worker's dependencies, with or without a registered sender.</summary>
        private static ServiceProvider Compose(bool withSender)
        {
            ServiceCollection services = new();
            services.AddSingleton<EmailDispatcher>();
            if (withSender)
            {
                // Scoped, like the platform's router — the guard must not depend on being able to
                // resolve it from the root provider.
                services.AddScoped<IEmailSender, StubSender>();
            }

            return services.BuildServiceProvider();
        }

        /// <summary>Constructs the worker over a composed container.</summary>
        private static EmailDispatchHostedService WorkerFor(ServiceProvider provider)
        {
            return new EmailDispatchHostedService(
                provider.GetRequiredService<EmailDispatcher>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IServiceProviderIsService>(),
                NullLogger<EmailDispatchHostedService>.Instance);
        }

        /// <summary>A sender that is never called; only its registration matters here.</summary>
        private sealed class StubSender : IEmailSender
        {
            public Task<IReadOnlyList<EmailSendResult>> SendAsync(
                IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The composition guard must not send anything.");
            }
        }
    }
}
