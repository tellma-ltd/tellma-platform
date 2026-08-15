// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Webhooks;
using Tellma.Core.Email.Tests.Infrastructure;

namespace Tellma.Core.Email.Tests.Selection
{
    /// <summary>
    ///     Everything the pipeline refuses to start with. Each of these is a deployment that would
    ///     otherwise fail on its first send, or silently send the wrong thing.
    /// </summary>
    public class StartupValidationTests
    {
        [Fact]
        public async Task Defaults_to_the_log_sink_in_development()
        {
            await using var host = EmailTestHost.Build();

            await host.StartAsync(TestContext.Current.CancellationToken);

            using IServiceScope scope = host.Services.CreateScope();
            Assert.IsType<EmailRouter>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        }

        [Fact]
        public async Task Refuses_to_start_without_a_provider_outside_development()
        {
            await using var host = EmailTestHost.Build(environmentName: Environments.Production);

            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains(failure.Failures, static f => f.Contains("Email:Provider is required", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Refuses_to_start_when_the_provider_matches_no_transport_and_names_the_alternatives()
        {
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "sendgird" },
                configure: services => services.AddFakeTransport("sendgrid", new FakeEmailSender()));

            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            // The message lists what is registered, which turns a typo into a one-glance fix.
            string message = Assert.Single(failure.Failures);
            Assert.Contains("sendgird", message, StringComparison.Ordinal);
            Assert.Contains("log-sink, sendgrid", message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Matches_the_provider_name_case_insensitively()
        {
            FakeEmailSender sendgrid = new();
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "  SendGrid " },
                configure: services => services.AddFakeTransport("sendgrid", sendgrid));

            await host.StartAsync(TestContext.Current.CancellationToken);

            // Starting is not the assertion: a selector that matched nothing and fell back to the
            // log sink would also start cleanly. What is asserted is that mail reaches the
            // transport whose name differed only in case and surrounding whitespace.
            using IServiceScope scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                .SendAsync(
                    [EmailTestMessages.Message(EmailAudience.External, "routed")],
                    TestContext.Current.CancellationToken);

            Assert.Equal("routed", Assert.Single(sendgrid.AllMessages).Subject);
        }

        [Fact]
        public async Task Refuses_two_transports_with_the_same_name()
        {
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "dup" },
                configure: services =>
                {
                    services.AddFakeTransport("dup", new FakeEmailSender());
                    services.AddFakeTransport("dup", new FakeEmailSender());
                });

            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains(failure.Failures, static f => f.Contains("More than one email transport", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Refuses_a_transport_name_that_is_not_lowercase_kebab_case()
        {
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "Bad_Name" },
                configure: services => services.AddFakeTransport("Bad_Name", new FakeEmailSender()));

            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains(failure.Failures, static f => f.Contains("lowercase kebab-case", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Refuses_the_log_sink_outside_development()
        {
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "log-sink" },
                environmentName: Environments.Staging);

            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            // Staging is deliberately included: it exists to be a faithful replica of production,
            // which a log sink is not.
            Assert.Contains(failure.Failures, static f => f.Contains("only permitted in the Development", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Refuses_to_start_without_a_deployment_identity()
        {
            await using var host = EmailTestHost.Build(registerSandboxContext: false);

            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains(failure.Failures, static f => f.Contains("DeploymentIdentity", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Refuses_to_start_without_a_sandbox_context()
        {
            await using var host = EmailTestHost.Build(
                deployment: new Abstractions.Hosting.DeploymentIdentity("etpharma", "Development"),
                registerSandboxContext: false);

            OptionsValidationException failure = await Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            // A composition that forgot to decide must fail, not silently treat sandbox tenants as
            // live.
            Assert.Contains(failure.Failures, static f => f.Contains("ISandboxContext", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Warms_only_the_active_transport_so_referencing_an_unconfigured_adapter_is_legal()
        {
            bool activeWarmed = false;
            bool inactiveWarmed = false;

            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "active" },
                configure: services =>
                {
                    services.AddSingleton(new EmailTransportRegistration("active", _ =>
                    {
                        activeWarmed = true;
                        return new FakeEmailSender();
                    }));

                    services.AddSingleton(new EmailTransportRegistration("inactive", _ =>
                    {
                        inactiveWarmed = true;
                        throw new InvalidOperationException("This adapter is not configured.");
                    }));
                });

            await host.StartAsync(TestContext.Current.CancellationToken);

            Assert.True(activeWarmed, "The active transport must be built at startup so its own options validate then.");
            Assert.False(inactiveWarmed, "An inactive transport must stay cold, or a distribution could not compile in every adapter.");
        }

        [Fact]
        public async Task Refuses_two_delivery_event_handlers_with_the_same_owner_key()
        {
            await using var host = EmailTestHost.Build(
                configure: services =>
                {
                    services.AddScoped<IEmailDeliveryEventHandler>(_ => new StubHandler("outbox"));
                    services.AddScoped<IEmailDeliveryEventHandler>(_ => new StubHandler("outbox"));
                });

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains("owner key 'outbox'", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Refuses_a_delivery_event_handler_whose_owner_key_is_malformed()
        {
            await using var host = EmailTestHost.Build(
                configure: services => services.AddScoped<IEmailDeliveryEventHandler>(_ => new StubHandler("Out_Box")));

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => host.StartAsync(TestContext.Current.CancellationToken));

            Assert.Contains("lowercase kebab-case", failure.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Starts_when_a_connector_registers_its_webhook_receiver()
        {
            // Receivers are scoped, because they depend on the scoped delivery-event dispatcher. The
            // startup gate reports whether the active transport's receiver exists, and a version that
            // took the receivers on its constructor made every deployment with delivery events fail
            // scope validation — which the test host now runs, exactly as Development does.
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "sendgrid" },
                configure: services =>
                {
                    services.AddFakeTransport("sendgrid", new FakeEmailSender());
                    services.AddScoped<IWebhookReceiver>(sp => new StubReceiver(
                        "sendgrid-events",
                        sp.GetRequiredService<IEmailDeliveryEventDispatcher>()));
                });

            await host.StartAsync(TestContext.Current.CancellationToken);
        }

        private sealed class StubReceiver(string key, IEmailDeliveryEventDispatcher dispatcher) : IWebhookReceiver
        {
            public string Key => key;

            public Task<WebhookResult> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
            {
                // The dispatcher is held only so this stub has the same scoped dependency the real
                // receivers have; nothing here dispatches.
                _ = dispatcher;
                return Task.FromResult(new WebhookResult(WebhookOutcome.Accepted));
            }
        }

        private sealed class StubHandler(string ownerKey) : IEmailDeliveryEventHandler
        {
            public string OwnerKey => ownerKey;

            public Task HandleAsync(IReadOnlyList<EmailDeliveryEvent> events, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
