// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Defaults.Azure.AspNetCore;

namespace Tellma.Defaults.Azure.Tests
{
    /// <summary>
    ///     The realtime line of the Azure web defaults: Azure SignalR Service selected when its
    ///     connection string is set, under the deployment's application name and with the hub-only
    ///     token's claims, and the hosting check's verdicts on the Azure mode.
    /// </summary>
    public sealed class AzureRealtimeTests
    {
        /// <summary>A connection string of the shape the service issues; nothing is ever sent to it.</summary>
        private const string ConnectionString = "Endpoint=https://acme.service.signalr.net;AccessKey=dGVzdC1rZXktdGVzdC1rZXktdGVzdC1rZXk=;Version=1.0;";

        [Fact]
        public async Task With_a_connection_string_the_hub_runs_on_the_service_under_the_deployment_s_name()
        {
            await using WebApplication app = Build(new() { [AzureWebDefaultsTellmaBuilderExtensions.SignalRConnectionStringKey] = ConnectionString });

            // --- The options: the deployment's application name, a short token with the hub claims ---
            ServiceOptions options = app.Services.GetRequiredService<IOptions<ServiceOptions>>().Value;
            Assert.Equal("acme_development", options.ApplicationName);
            Assert.Equal(TimeSpan.FromMinutes(5), options.AccessTokenLifetime);
            ClaimsPrincipal user = new(new ClaimsIdentity([new Claim("sub", "sub-9"), new Claim("email", "ann@example.com")], "Tellma.Session"));
            user.AddIdentity(new ClaimsIdentity(
                [new Claim(TellmaHubClaims.TenantId, "1"), new Claim(TellmaHubClaims.UserId, "5"), new Claim(TellmaHubClaims.SessionHandle, "h")],
                TellmaHubClaims.AuthenticationType));
            Assert.Equal(
                ["sub", TellmaHubClaims.TenantId, TellmaHubClaims.UserId, TellmaHubClaims.SessionHandle],
                options.ClaimsProvider!(new DefaultHttpContext { User = user }).Select(static claim => claim.Type));

            // --- The hosting check, which would refuse a connection string with the hub not on the service ---
            Assert.Empty(await RunHostingCheckAsync(app));
        }

        [Fact]
        public async Task Without_a_connection_string_the_hub_runs_in_process()
        {
            await using WebApplication app = Build([]);

            Assert.Empty(await RunHostingCheckAsync(app));
        }

        [Fact]
        public async Task Another_application_name_fails_the_hosting_check()
        {
            await using WebApplication app = Build(
                new() { [AzureWebDefaultsTellmaBuilderExtensions.SignalRConnectionStringKey] = ConnectionString },
                static services => services.PostConfigure<ServiceOptions>(static options => options.ApplicationName = "acme_production"));

            CompositionProblem problem = Assert.Single(await RunHostingCheckAsync(app));
            Assert.Contains("'acme_production'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("'acme_development'", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_connection_string_the_web_defaults_did_not_compose_fails_the_hosting_check()
        {
            WebApplicationBuilder builder = Builder(new() { [AzureWebDefaultsTellmaBuilderExtensions.SignalRConnectionStringKey] = ConnectionString });
            _ = builder.AddTellma("acme", static _ => { });
            await using WebApplication app = builder.Build();

            CompositionProblem problem = Assert.Single(await RunHostingCheckAsync(app));
            Assert.Contains("Azure:SignalR:ConnectionString is set", problem.Problem, StringComparison.Ordinal);
        }

        /// <summary>A web host composing the Azure web defaults in its web delegate, as a distribution does; built, never started.</summary>
        /// <param name="values">The configuration.</param>
        /// <param name="configureServices">More services, after the composition.</param>
        /// <returns>The application.</returns>
        private static WebApplication Build(Dictionary<string, string?> values, Action<IServiceCollection>? configureServices = null)
        {
            WebApplicationBuilder builder = Builder(values);
            _ = builder.AddTellma("acme", static _ => { }, static web => web.UseAzureWebDefaults());
            configureServices?.Invoke(builder.Services);
            return builder.Build();
        }

        /// <summary>A Development web host builder over a configuration.</summary>
        /// <param name="values">The configuration.</param>
        /// <returns>The builder.</returns>
        private static WebApplicationBuilder Builder(Dictionary<string, string?> values)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ApplicationName = "Tellma.Defaults.Azure.Tests",
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.Configuration.Sources.Clear();
            _ = builder.Configuration.AddInMemoryCollection(values);
            return builder;
        }

        /// <summary>Runs the realtime hosting check alone.</summary>
        /// <param name="app">The application.</param>
        /// <returns>What it found.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> RunHostingCheckAsync(WebApplication app)
        {
            IStartupCheck check = Assert.Single(app.Services.GetServices<IStartupCheck>(), static check => check.Name == "host.realtime-hosting");
            return check.CheckAsync(app.Services, TestContext.Current.CancellationToken);
        }
    }
}
