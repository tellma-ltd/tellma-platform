// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Testing.Support.Tenancy;

namespace Tellma.Core.Email.Tests.Infrastructure
{
    /// <summary>
    ///     Builds the email composition the way a host does, so the startup gates the pipeline relies
    ///     on are exercised rather than bypassed.
    /// </summary>
    public sealed class EmailTestHost : IAsyncDisposable
    {
        private EmailTestHost(ServiceProvider services, StubSandboxContext sandbox)
        {
            Services = services;
            Sandbox = sandbox;
        }

        /// <summary>The built container.</summary>
        public ServiceProvider Services { get; }

        /// <summary>The sandbox flag the router consults; settable per test.</summary>
        public StubSandboxContext Sandbox { get; }

        /// <summary>Builds a composition.</summary>
        /// <param name="settings">Configuration entries, e.g. <c>Email:Provider</c>.</param>
        /// <param name="environmentName">The host environment; Development by default.</param>
        /// <param name="configure">Extra registrations, typically the fake transports.</param>
        /// <param name="deployment">The deployment identity, or null to omit it entirely.</param>
        /// <param name="registerSandboxContext">False to omit the sandbox seam, so the startup gate
        ///     that demands it can be observed.</param>
        /// <returns>The host.</returns>
        public static EmailTestHost Build(
            IDictionary<string, string?>? settings = null,
            string? environmentName = null,
            Action<IServiceCollection>? configure = null,
            DeploymentIdentity? deployment = null,
            bool registerSandboxContext = true)
        {
            environmentName ??= Environments.Development;

            ServiceCollection services = new();
            StubSandboxContext sandbox = new();

            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
            services.AddLogging();

            if (deployment is not null || registerSandboxContext)
            {
                services.AddSingleton(deployment ?? new DeploymentIdentity("etpharma", environmentName));
            }

            if (registerSandboxContext)
            {
                services.AddScoped<ISandboxContext>(_ => sandbox);
            }

            services.AddTellmaEmail();
            configure?.Invoke(services);

            // Scope validation, as a WebApplicationBuilder turns on in Development: a singleton that
            // captures a scoped service fails here rather than in someone's dev loop. Build-time
            // validation is deliberately left off, because several cases below omit a registration on
            // purpose to observe the pipeline's own diagnostic rather than the container's.
            return new EmailTestHost(
                services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }),
                sandbox);
        }

        /// <summary>
        ///     Runs the startup gates in the order a real host runs them: options validation first,
        ///     then the hosted services.
        /// </summary>
        /// <param name="cancellationToken">Abandons the startup.</param>
        /// <returns>A task that completes when startup has run.</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Services.GetRequiredService<IStartupValidator>().Validate();

            foreach (IHostedService hostedService in Services.GetServices<IHostedService>())
            {
                await hostedService.StartAsync(cancellationToken);
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return Services.DisposeAsync();
        }

        private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = environmentName;

            public string ApplicationName { get; set; } = "Tellma.Core.Email.Tests";

            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            public IFileProvider ContentRootFileProvider { get; set; } =
                new PhysicalFileProvider(AppContext.BaseDirectory);
        }
    }
}
