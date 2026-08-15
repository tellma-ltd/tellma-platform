// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Email;
using Tellma.Core.Testing.Email;

namespace Tellma.Core.Testing.Tests.Email
{
    /// <summary>
    ///     The two registration modes, and the difference that makes them worth having: one bypasses
    ///     the routing policy, the other keeps it in the loop.
    /// </summary>
    public class AddCapturingEmailTests
    {
        [Fact]
        public async Task Replaces_the_sender_outright_in_the_simple_mode()
        {
            ServiceCollection services = Composition();
            CapturingEmailSender capturing = services.AddCapturingEmail();

            await using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            IEmailSender sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            Assert.Same(capturing, sender);

            await sender.SendAsync([Message(EmailAudience.External)], TestContext.Current.CancellationToken);
            Assert.Single(capturing.Captured);
        }

        [Fact]
        public async Task Keeps_the_router_and_its_sandbox_marking_in_the_loop_in_transport_mode()
        {
            ServiceCollection services = Composition(sandboxTenant: true);
            CapturingEmailSender capturing = services.AddCapturingEmail(CapturingEmailMode.Transport);

            await using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();

            IReadOnlyList<EmailSendResult> results = await scope.ServiceProvider
                .GetRequiredService<IEmailSender>()
                .SendAsync(
                    [Message(EmailAudience.Internal), Message(EmailAudience.External)],
                    TestContext.Current.CancellationToken);

            // The marking a sandbox tenant's internal mail gets is exactly what production would do,
            // which is the point of running the pipeline rather than replacing it.
            CapturedEmail marked = capturing.Captured[0];
            Assert.StartsWith("[Sandbox] ", marked.Message.Subject, StringComparison.Ordinal);

            // The Sent-to-Sandboxed rewrite is visible in what the caller was told, while the
            // capture holds what the sender itself reported.
            Assert.Equal(EmailSendOutcome.Sandboxed, results[1].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, capturing.Captured[1].Result.Outcome);
        }

        private static ServiceCollection Composition(bool sandboxTenant = false)
        {
            ServiceCollection services = new();

            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Provider"] = CapturingEmailServiceCollectionExtensions.TransportName,
                })
                .Build());

            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
            services.AddLogging();
            services.AddSingleton(new DeploymentIdentity("etpharma", "Development"));
            services.AddScoped<ISandboxContext>(_ => new FixedSandboxContext(sandboxTenant));
            services.AddTellmaEmail();

            return services;
        }

        private static EmailMessage Message(EmailAudience audience)
        {
            return new EmailMessage
            {
                To = [new EmailAddress("recipient@example.com")],
                Subject = "Subject",
                TextBody = "Body",
                Audience = audience,
            };
        }

        private sealed class FixedSandboxContext(bool isSandbox) : ISandboxContext
        {
            public bool IsSandbox => isSandbox;
        }

        private sealed class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;

            public string ApplicationName { get; set; } = "Tellma.Core.Testing.Tests";

            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            public IFileProvider ContentRootFileProvider { get; set; } =
                new PhysicalFileProvider(AppContext.BaseDirectory);
        }
    }
}
