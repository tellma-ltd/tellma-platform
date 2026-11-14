// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellma.Core.AspNetCore;
using Tellma.Core.Testing.Email;
using Tellma.Distro.Acme.Web;
using Tellma.Identity.Services.BackchannelLogout;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Acme's web host on a test server, built call for call as its <c>Program.cs</c> builds it,
    ///     over Acme's own configuration files and a deployment's settings. Everything the host
    ///     reaches over HTTP at its own public origin, the identity engine it mounts in process
    ///     included, is answered by the test server: the OpenID Connect handler's back channel and the
    ///     engine's back-channel logout client. Mail goes to a capturing transport.
    /// </summary>
    internal sealed class AcmeHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        /// <summary>Initializes a host over a built application.</summary>
        /// <param name="app">The application.</param>
        /// <param name="emails">The capturing transport.</param>
        /// <param name="warnings">The host's warnings and errors.</param>
        private AcmeHost(WebApplication app, CapturingEmailSender emails, WarningLog warnings)
        {
            _app = app;
            Emails = emails;
            Warnings = warnings;
        }

        /// <summary>The mail the host sent, sign-in codes among it.</summary>
        public CapturingEmailSender Emails { get; }

        /// <summary>The host's warnings and errors, for a failed assertion to report.</summary>
        public WarningLog Warnings { get; }

        /// <summary>The host's services.</summary>
        public IServiceProvider Services => _app.Services;

        /// <summary>The test server.</summary>
        public TestServer Server => _app.GetTestServer();

        /// <summary>
        ///     Where the engine's back-channel logout tokens go; null delivers them to the host itself,
        ///     as a deployment's engine would.
        /// </summary>
        public HttpMessageHandler? BackchannelLogoutSink { get; set; }

        /// <summary>Builds and starts a host.</summary>
        /// <param name="values">The deployment's settings, over Acme's configuration files.</param>
        /// <param name="environment">The host environment.</param>
        /// <param name="services">Registers what stands in for the rest of a deployment, after everything else.</param>
        /// <returns>The started host.</returns>
        public static async Task<AcmeHost> StartAsync(
            IReadOnlyDictionary<string, string?> values, string environment = "Development", Action<IServiceCollection>? services = null)
        {
            AcmeHost host = Build(values, environment, services);
            await host._app.StartAsync(TestContext.Current.CancellationToken);
            return host;
        }

        /// <summary>Builds a host without starting it.</summary>
        /// <param name="values">The deployment's settings, over Acme's configuration files.</param>
        /// <param name="environment">The host environment.</param>
        /// <param name="services">Registers what stands in for the rest of a deployment, after everything else.</param>
        /// <returns>The built host.</returns>
        public static AcmeHost Build(
            IReadOnlyDictionary<string, string?> values, string environment = "Development", Action<IServiceCollection>? services = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = typeof(AcmeWebHostMarker).Assembly.GetName().Name,

                // Acme's appsettings.json and appsettings.Development.json, copied beside the suite.
                ContentRootPath = AppContext.BaseDirectory,
            });
            _ = builder.WebHost.UseTestServer();

            // The deployment's settings come last, so they win over the files and over anything a
            // developer's checkout derives.
            _ = builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(values)
            {
                ["Email:Provider"] = CapturingEmailServiceCollectionExtensions.TransportName,
            });
            // --- Program.cs, call for call ---
            _ = builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, AcmeWebComposition.Compose);

            // The platform's logging replaces every provider registered before it and forwards its
            // events to those registered after, so the capture joins here.
            WarningLog warnings = new();
            _ = builder.Services.AddSingleton<ILoggerProvider>(warnings);

            // --- The test server stands in for the network ---
            CapturingEmailSender emails = builder.Services.AddCapturingEmail(CapturingEmailMode.Transport);
            AcmeHost? host = null;
            _ = builder.Services.Configure<OpenIdConnectOptions>(
                TellmaAuthentication.OidcScheme, oidc => oidc.BackchannelHttpHandler = new ServerHandler(() => host!.Server.CreateHandler()));
            _ = builder.Services.AddHttpClient(BackchannelLogoutService.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new ServerHandler(() => host!.BackchannelLogoutSink ?? host.Server.CreateHandler()));
            services?.Invoke(builder.Services);

            WebApplication app = builder.Build();
            _ = app.UseTellma();
            _ = app.MapTellma();
            host = new AcmeHost(app, emails, warnings);
            return host;
        }

        /// <summary>Starts a built host.</summary>
        /// <returns>A task that completes when the host started.</returns>
        public Task StartAsync()
        {
            return _app.StartAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Creates a client at the public origin, as the identity flow driver asks for one: with
        ///     cookies when the options say so, never following a redirect on its own.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>The client.</returns>
        public HttpClient CreateClient(WebApplicationFactoryClientOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            HttpMessageHandler handler = Server.CreateHandler();
            if (options.HandleCookies)
            {
                handler = new CookieContainerHandler { InnerHandler = handler };
            }

            return new HttpClient(handler) { BaseAddress = new Uri(AcmeMigrator.PublicOrigin) };
        }

        /// <summary>A protocol driver whose browser holds cookies at the public origin, with the engine at its path base.</summary>
        /// <returns>The driver; the caller disposes it.</returns>
        public OidcFlowClient CreateFlowClient()
        {
            return new OidcFlowClient(CreateClient, Emails, "/id");
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }

        /// <summary>A handler that forwards to one created on first use, once the test server exists.</summary>
        /// <param name="create">Creates the handler forwarded to.</param>
        private sealed class ServerHandler(Func<HttpMessageHandler> create) : HttpMessageHandler
        {
            private HttpMessageInvoker? _invoker;

            /// <inheritdoc />
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _invoker ??= new HttpMessageInvoker(create());
                return _invoker.SendAsync(request, cancellationToken);
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _invoker?.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
