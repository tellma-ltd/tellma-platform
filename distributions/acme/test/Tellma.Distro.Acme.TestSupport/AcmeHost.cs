// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using Tellma.Core.AspNetCore;
using Tellma.Core.Identity;
using Tellma.Core.Tenancy;
using Tellma.Core.Testing.Blobs;
using Tellma.Core.Testing.Email;
using Tellma.Distro.Acme.Web;
using Tellma.Identity.Services.BackchannelLogout;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.TestSupport
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

        /// <summary>Whether starting the host waits until its tenant snapshot is loaded.</summary>
        private readonly bool _awaitsCatalog;

        /// <summary>Initializes a host over a built application.</summary>
        /// <param name="app">The application.</param>
        /// <param name="awaitsCatalog">Whether starting the host waits until its tenant snapshot is loaded.</param>
        /// <param name="emails">The capturing transport.</param>
        /// <param name="warnings">The host's warnings and errors.</param>
        /// <param name="probes">What the tenant probes record; null when the host maps none.</param>
        private AcmeHost(WebApplication app, bool awaitsCatalog, CapturingEmailSender emails, WarningLog warnings, TenantProbeLog? probes)
        {
            _app = app;
            _awaitsCatalog = awaitsCatalog;
            Emails = emails;
            Warnings = warnings;
            Probes = probes;
        }

        /// <summary>The mail the host sent, sign-in codes among it.</summary>
        public CapturingEmailSender Emails { get; }

        /// <summary>The host's warnings and errors, for a failed assertion to report.</summary>
        public WarningLog Warnings { get; }

        /// <summary>What the tenant probes recorded; null when the host maps none.</summary>
        public TenantProbeLog? Probes { get; }

        /// <summary>The host's services.</summary>
        public IServiceProvider Services => _app.Services;

        /// <summary>The test server; unavailable on a host that listens with Kestrel.</summary>
        public TestServer Server => _app.GetTestServer();

        /// <summary>
        ///     Where a client sends its requests: the public origin, which the test server answers, or
        ///     the loopback address Kestrel listens on.
        /// </summary>
        public Uri Address => _app.Services.GetRequiredService<IServer>() is TestServer
            ? new Uri(AcmeMigrator.PublicOrigin)
            : new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());

        /// <summary>
        ///     Where the engine's back-channel logout tokens go; null delivers them to the host itself,
        ///     as a deployment's engine would.
        /// </summary>
        public HttpMessageHandler? BackchannelLogoutSink { get; set; }

        /// <summary>
        ///     Builds and starts a host and, unless <see cref="AcmeHostOptions.AwaitsCatalog" /> is
        ///     off, waits until its tenant snapshot is loaded.
        /// </summary>
        /// <param name="values">The deployment's settings, over Acme's configuration files.</param>
        /// <param name="options">What the test adds to the host; null for nothing.</param>
        /// <returns>The started host.</returns>
        public static async Task<AcmeHost> StartAsync(IReadOnlyDictionary<string, string?> values, AcmeHostOptions? options = null)
        {
            AcmeHost host = Build(values, options);
            try
            {
                await host.StartAsync();
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }

            return host;
        }

        /// <summary>Builds a host without starting it.</summary>
        /// <param name="values">The deployment's settings, over Acme's configuration files.</param>
        /// <param name="options">What the test adds to the host; null for nothing.</param>
        /// <returns>The built host.</returns>
        public static AcmeHost Build(IReadOnlyDictionary<string, string?> values, AcmeHostOptions? options = null)
        {
            options ??= new AcmeHostOptions();
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = options.Environment,
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

            // --- Program.cs, call for call, with the test's further composition beside Acme's ---
            _ = builder.AddTellma(
                AcmeComposition.Slug,
                tellma =>
                {
                    AcmeComposition.Compose(tellma);
                    options.Compose?.Invoke(tellma);
                },
                AcmeWebComposition.Compose);

            // The platform's logging replaces every provider registered before it and forwards its
            // events to those registered after, so the capture joins here.
            WarningLog warnings = new();
            _ = builder.Services.AddSingleton<ILoggerProvider>(warnings);

            // --- The test server stands in for the network ---
            CapturingEmailSender emails = builder.Services.AddCapturingEmail(CapturingEmailMode.Transport);
            AcmeHost? host = null;
            _ = builder.Services.Configure<OpenIdConnectOptions>(
                TellmaAuthentication.OidcScheme, oidc => oidc.BackchannelHttpHandler = new ServerHandler(() => host!.Server.CreateHandler()));
            _ = builder.Services.Configure<JwtBearerOptions>(
                TellmaAuthentication.BearerScheme, bearer => bearer.BackchannelHttpHandler = new ServerHandler(() => host!.Server.CreateHandler()));
            _ = builder.Services.AddHttpClient(BackchannelLogoutService.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new ServerHandler(() => host!.BackchannelLogoutSink ?? host.Server.CreateHandler()));

            // The platform's identity-server client reaches the in-proc engine at the public origin too.
            _ = builder.Services.AddHttpClient(IdentityServerClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new ServerHandler(() => host!.Server.CreateHandler()));

            // --- Blobs in memory, so a suite never writes under the developer's application data,
            // unless the test keeps the composed store under a root of its own ---
            if (!options.KeepsComposedBlobs)
            {
                _ = builder.Services.AddInMemoryBlobs();
            }

            // --- The tenant probes' record, one per host, so two hosts of one test keep theirs apart ---
            TenantProbeLog? probes = options.MapsProbes ? new TenantProbeLog() : null;
            if (probes is not null)
            {
                _ = builder.Services.AddSingleton(probes);
            }

            options.Services?.Invoke(builder.Services);

            // --- A real Kestrel listener on a loopback port in place of the test server ---
            // The defaults register Kestrel before the test server replaces it as the last server
            // registered; removing the test server's registration leaves Kestrel's.
            if (options.UsesKestrel)
            {
                foreach (ServiceDescriptor testServer in builder.Services.Where(static descriptor => descriptor.ServiceType == typeof(IServer) && descriptor.ImplementationType == typeof(TestServer)).ToList())
                {
                    _ = builder.Services.Remove(testServer);
                }

                _ = builder.WebHost.ConfigureKestrel(static kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            }

            WebApplication app = builder.Build();
            _ = app.UseTellma();
            TellmaEndpoints groups = app.MapTellma();

            // --- The test's own tenant endpoints, mapped onto the groups as a distribution maps its own ---
            if (probes is not null)
            {
                TenantProbes.Map(groups);
            }

            options.Map?.Invoke(groups);

            host = new AcmeHost(app, options.AwaitsCatalog, emails, warnings, probes);
            return host;
        }

        /// <summary>
        ///     Starts a built host and, unless its <see cref="AcmeHostOptions.AwaitsCatalog" /> was off,
        ///     waits until its tenant snapshot is loaded.
        /// </summary>
        /// <returns>A task that completes when the host started and, when it waits, its snapshot is available.</returns>
        public async Task StartAsync()
        {
            await _app.StartAsync(TestContext.Current.CancellationToken);

            // A host whose first catalog load outlasts its startup bound starts not ready, and a
            // load balancer sends it nothing until readiness turns: wait for the snapshot the same
            // way, joining the load still in flight, rather than racing it with the first request.
            if (_awaitsCatalog)
            {
                await AwaitCatalogAsync();
            }
        }

        /// <summary>
        ///     Waits until the host's tenant snapshot may be served: at once when it is, else for the
        ///     refresh in flight, or one started now when none is.
        /// </summary>
        /// <returns>A task that completes when the snapshot is available, failing as the refresh failed.</returns>
        public async Task AwaitCatalogAsync()
        {
            ITenantRegistry registry = Services.GetRequiredService<ITenantRegistry>();
            if (!registry.IsAvailable)
            {
                await registry.RefreshAsync(force: false, TestContext.Current.CancellationToken);
            }

            Assert.True(registry.IsAvailable, "The host's tenant snapshot is not available after a successful refresh.");
        }

        /// <summary>A handler that reaches the host: the test server's, or one over the network to Kestrel's port.</summary>
        /// <returns>The handler; the caller disposes it.</returns>
        public HttpMessageHandler CreateHandler()
        {
            return _app.Services.GetRequiredService<IServer>() is TestServer server ? server.CreateHandler() : new SocketsHttpHandler();
        }

        /// <summary>
        ///     Stops the host as its orchestrator stops an instance: it stops accepting, and waits for
        ///     the requests in flight up to the host's shutdown timeout.
        /// </summary>
        /// <returns>A task that completes when the host stopped.</returns>
        public Task StopAsync()
        {
            return _app.StopAsync(CancellationToken.None);
        }

        /// <summary>
        ///     A protocol driver whose browser holds cookies at the public origin, with the engine at its
        ///     path base.
        /// </summary>
        /// <param name="cookies">
        ///     The browser's cookie jar, which another host's browser may share, as one browser reaches
        ///     every instance of a deployment; null for a jar of its own.
        /// </param>
        /// <returns>The driver; the caller disposes it.</returns>
        public OidcFlowClient CreateFlowClient(CookieContainer? cookies = null)
        {
            CookieContainer jar = cookies ?? new CookieContainer();
            return new OidcFlowClient(options => CreateClient(options, jar), Emails, "/id");
        }

        /// <summary>
        ///     A browser at the public origin over a cookie jar another host's browser filled, as a
        ///     deployment's load balancer sends one browser to another instance.
        /// </summary>
        /// <param name="cookies">The cookie jar.</param>
        /// <returns>The client; the caller disposes it.</returns>
        public HttpClient CreateBrowser(CookieContainer cookies)
        {
            ArgumentNullException.ThrowIfNull(cookies);
            return CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }, cookies);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }

        /// <summary>
        ///     Creates a client at the public origin, as the identity flow driver asks for one: with
        ///     cookies when the options say so, never following a redirect on its own.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="cookies">The cookie jar a client that handles cookies keeps them in.</param>
        /// <returns>The client.</returns>
        private HttpClient CreateClient(WebApplicationFactoryClientOptions options, CookieContainer cookies)
        {
            ArgumentNullException.ThrowIfNull(options);
            HttpMessageHandler handler = Server.CreateHandler();
            if (options.HandleCookies)
            {
                handler = new CookieContainerHandler(cookies) { InnerHandler = handler };
            }

            return new HttpClient(handler) { BaseAddress = new Uri(AcmeMigrator.PublicOrigin) };
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
