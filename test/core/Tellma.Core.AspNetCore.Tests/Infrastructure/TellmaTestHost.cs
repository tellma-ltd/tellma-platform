// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Surfaces;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>
    ///     A distribution's web host on a test server: the real <c>AddTellma</c>, <c>UseTellma</c> and
    ///     <c>MapTellma</c> over an in-memory tenant catalog, a scripted user connection, a static
    ///     identity server and the probe endpoints of <see cref="ProbeEndpoints" />.
    /// </summary>
    internal sealed class TellmaTestHost : IAsyncDisposable
    {
        /// <summary>The distribution's slug.</summary>
        public const string Slug = "acme";

        /// <summary>The distribution's public origin.</summary>
        public const string PublicOrigin = "https://acme.app.tellma.com";

        /// <summary>The identity server's issuer.</summary>
        public const string Issuer = "https://identity.test";

        /// <summary>The user id the scripted connection binds every member to.</summary>
        public const int UserId = 5;

        /// <summary>The subject the scripted connection refuses as no member of any tenant.</summary>
        public const string Stranger = "stranger";

        /// <summary>The key every test token is signed with.</summary>
        private static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key" };

        private readonly WebApplication _app;

        private TellmaTestHost(WebApplication app, FakeCatalogReader catalog, FakeTimeProvider time)
        {
            _app = app;
            Catalog = catalog;
            Time = time;
            Client = app.GetTestClient();
        }

        /// <summary>A client of the host, at <c>https://localhost</c>.</summary>
        public HttpClient Client { get; }

        /// <summary>The host's services.</summary>
        public IServiceProvider Services => _app.Services;

        /// <summary>The in-memory catalog the registry reads.</summary>
        public FakeCatalogReader Catalog { get; }

        /// <summary>The host's clock.</summary>
        public FakeTimeProvider Time { get; }

        /// <summary>The settings every host starts from.</summary>
        /// <returns>A fresh copy.</returns>
        public static Dictionary<string, string?> SoundSettings()
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:PublicOrigin"] = PublicOrigin,
                ["Tellma:Catalog:ConnectionString"] = "Data Source=sql01.database.windows.net;Initial Catalog=tellma-acme-catalog;Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=10",
                ["Tellma:Sql:Profiles:default"] = "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20",
                ["Tellma:Sql:AllowedServers:0"] = ".database.windows.net",
                ["Tellma:DisplayName"] = "Acme ERP",
                ["Tellma:Identity:Authority"] = Issuer,
                ["Tellma:DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "tellma-aspnetcore-tests-keys"),
            };
        }

        /// <summary>Builds a host without starting it, for a test that expects startup to fail.</summary>
        /// <param name="configure">Changes to the defaults.</param>
        /// <returns>The application and its parts.</returns>
        public static (WebApplication App, FakeCatalogReader Catalog, FakeTimeProvider Time) Build(Action<TestHostOptions>? configure = null)
        {
            TestHostOptions options = new();
            configure?.Invoke(options);

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = options.Environment,
                ApplicationName = typeof(TellmaTestHost).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                WebRootPath = options.WebRoot,
            });
            _ = builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            _ = builder.Configuration.AddInMemoryCollection(options.Settings);

            // --- What stands in for the infrastructure: the catalog, the clock, the user connection ---
            FakeCatalogReader catalog = new();
            catalog.Write([.. options.Rows]);
            catalog.Failure = options.CatalogFailure;
            FakeTimeProvider time = new(DateTimeOffset.UtcNow);
            _ = builder.Services.AddSingleton<ICatalogReader>(catalog);
            _ = builder.Services.AddSingleton<TimeProvider>(time);
            _ = builder.Services.AddScoped<IRequestContextInitializer, ConnectingInitializer>();

            // --- The composition under test ---
            _ = builder.AddTellma(Slug, static _ => { });

            // --- The identity server: a static issuer and key, so no discovery document is fetched ---
            _ = builder.Services.Configure<JwtBearerOptions>(TellmaAuthentication.BearerScheme, static bearer =>
            {
                OpenIdConnectConfiguration configuration = new() { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                bearer.Configuration = configuration;
            });
            if (options.ManifestPath is not null)
            {
                _ = builder.Services.Configure<SpaHostingOptions>(spa => spa.ManifestPath = options.ManifestPath);
            }

            options.ConfigureServices?.Invoke(builder.Services);

            // --- The pipeline and the routes ---
            WebApplication app = builder.Build();
            if (options.UseTellma)
            {
                _ = app.UseTellma();
            }

            options.BeforeMapTellma?.Invoke(app);
            TellmaEndpoints endpoints = app.MapTellma();
            if (options.MapProbes)
            {
                ProbeEndpoints.Map(app, endpoints);
            }

            options.Map?.Invoke(app, endpoints);
            return (app, catalog, time);
        }

        /// <summary>Builds and starts a host.</summary>
        /// <param name="configure">Changes to the defaults.</param>
        /// <returns>The started host.</returns>
        public static async Task<TellmaTestHost> StartAsync(Action<TestHostOptions>? configure = null)
        {
            (WebApplication app, FakeCatalogReader catalog, FakeTimeProvider time) = Build(configure);
            await app.StartAsync(TestContext.Current.CancellationToken);
            return new TellmaTestHost(app, catalog, time);
        }

        /// <summary>A session cookie for a signed-in user, as the cookie scheme protects it.</summary>
        /// <param name="subject">The user's subject.</param>
        /// <param name="acr">The authentication context class the session reached.</param>
        /// <param name="authTime">When the user signed in.</param>
        /// <param name="handle">The session's handle.</param>
        /// <param name="sessionId">The identity server's session.</param>
        /// <returns>The <c>Cookie</c> header's value.</returns>
        public string Cookie(string subject = "sub-1", string? acr = null, DateTimeOffset? authTime = null, string? handle = null, string? sessionId = "sid-1")
        {
            List<Claim> claims = [new("sub", subject), new("azp", Slug)];
            if (sessionId is not null)
            {
                claims.Add(new Claim("sid", sessionId));
            }

            if (acr is not null)
            {
                claims.Add(new Claim("acr", acr));
            }

            if (authTime is { } time)
            {
                claims.Add(new Claim("auth_time", time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
            }

            AuthenticationProperties properties = new();
            if (handle is not null)
            {
                properties.Items[TellmaAuthentication.SessionHandleProperty] = handle;
            }

            CookieAuthenticationOptions cookie = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(TellmaAuthentication.SessionScheme);
            ClaimsPrincipal principal = new(new ClaimsIdentity(claims, TellmaAuthentication.SessionScheme, "sub", null));
            string value = cookie.TicketDataFormat.Protect(new AuthenticationTicket(principal, properties, TellmaAuthentication.SessionScheme));
            return $"{cookie.Cookie.Name}={value}";
        }

        /// <summary>A bearer access token from the test issuer.</summary>
        /// <param name="claims">The token's claims.</param>
        /// <param name="type">The token's type header.</param>
        /// <returns>The token.</returns>
        public static string Token(IDictionary<string, object> claims, string type = "at+jwt")
        {
            DateTime now = DateTime.UtcNow;
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Claims = claims,
                IssuedAt = now,
                NotBefore = now.AddMinutes(-1),
                Expires = now.AddMinutes(10),
                TokenType = type,
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            });
        }

        /// <summary>A public API token for a user: the API scope, the public origin's audience.</summary>
        /// <param name="subject">The user's subject.</param>
        /// <param name="audience">The audience.</param>
        /// <param name="scope">The scopes.</param>
        /// <returns>The token.</returns>
        public static string UserToken(string subject = "sub-1", string audience = PublicOrigin, string scope = "tellma_api")
        {
            return Token(new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["azp"] = "cli",
                ["aud"] = audience,
                ["scope"] = scope,
                ["tellma_kind"] = "individual",
                ["auth_time"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds(),
            });
        }

        /// <summary>A token for a service: no subject of a person, the client its subject.</summary>
        /// <param name="client">The client.</param>
        /// <param name="audience">The audience.</param>
        /// <param name="scope">The scopes.</param>
        /// <returns>The token.</returns>
        public static string ServiceToken(string client = "svc", string audience = PublicOrigin, string scope = "tellma_api")
        {
            return Token(new Dictionary<string, object>
            {
                ["sub"] = client,
                ["client_id"] = client,
                ["aud"] = audience,
                ["scope"] = scope,
                ["tellma_kind"] = "service",
            });
        }

        /// <summary>A request with the headers the SPA sends on every call.</summary>
        /// <param name="method">The method.</param>
        /// <param name="path">The path.</param>
        /// <param name="cookie">The session cookie, if any.</param>
        /// <param name="json">A JSON body, if any.</param>
        /// <returns>The request.</returns>
        public static HttpRequestMessage SpaRequest(HttpMethod method, string path, string? cookie, string? json = null)
        {
            HttpRequestMessage request = new(method, path);
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Origin", PublicOrigin);
            request.Headers.Add(TellmaAuthentication.CsrfHeaderName, "web/1.0.0");
            if (cookie is not null)
            {
                request.Headers.Add("Cookie", cookie);
            }

            if (json is not null)
            {
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            return request;
        }

        /// <summary>A request with a bearer token.</summary>
        /// <param name="method">The method.</param>
        /// <param name="path">The path.</param>
        /// <param name="token">The token.</param>
        /// <returns>The request.</returns>
        public static HttpRequestMessage BearerRequest(HttpMethod method, string path, string token)
        {
            HttpRequestMessage request = new(method, path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    /// <summary>What a test changes about its host.</summary>
    internal sealed class TestHostOptions
    {
        /// <summary>The host environment.</summary>
        public string Environment { get; set; } = "Production";

        /// <summary>The configuration.</summary>
        public Dictionary<string, string?> Settings { get; } = TellmaTestHost.SoundSettings();

        /// <summary>The catalog's tenants.</summary>
        public List<TenantRow> Rows { get; } = [FakeCatalogReader.Row(1)];

        /// <summary>A failure every catalog read throws, from the first load on.</summary>
        public Exception? CatalogFailure { get; set; }

        /// <summary>Whether the probe endpoints are mapped.</summary>
        public bool MapProbes { get; set; } = true;

        /// <summary>The web root, for the SPA's files.</summary>
        public string? WebRoot { get; set; }

        /// <summary>The static web assets manifest.</summary>
        public string? ManifestPath { get; set; }

        /// <summary>More services, registered after the composition.</summary>
        public Action<IServiceCollection>? ConfigureServices { get; set; }

        /// <summary>Whether <c>UseTellma</c> installs the pipeline before <c>MapTellma</c> runs.</summary>
        public bool UseTellma { get; set; } = true;

        /// <summary>Endpoints mapped before <c>MapTellma</c>, so its own audit reads them.</summary>
        public Action<WebApplication>? BeforeMapTellma { get; set; }

        /// <summary>More endpoints, mapped after <c>MapTellma</c>.</summary>
        public Action<WebApplication, TellmaEndpoints>? Map { get; set; }
    }

    /// <summary>
    ///     The user connection: every subject but <see cref="TellmaTestHost.Stranger" /> is a member,
    ///     bound to <see cref="TellmaTestHost.UserId" />.
    /// </summary>
    internal sealed class ConnectingInitializer : IRequestContextInitializer
    {
        /// <inheritdoc />
        public int Order => 100;

        /// <inheritdoc />
        public Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Subject == TellmaTestHost.Stranger
                ? throw new TenantNotFoundException(context.RequireTenant().Id)
                : Task.FromResult(context with { UserId = TellmaTestHost.UserId });
        }
    }
}
