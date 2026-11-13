// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Surfaces;
using Tellma.Core.AspNetCore.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>A session the test host stored: its cookie, its key and its handle.</summary>
    /// <param name="Cookie">The <c>Cookie</c> header's value.</param>
    /// <param name="Key">The session's key.</param>
    /// <param name="Handle">The session's handle.</param>
    internal sealed record TestSession(string Cookie, string Key, string Handle);

    /// <summary>
    ///     A distribution's web host on a test server: the real <c>AddTellma</c>, <c>UseTellma</c> and
    ///     <c>MapTellma</c> over an in-memory tenant catalog and session table, a scripted user
    ///     connection, a static identity server and the probe endpoints of <see cref="ProbeEndpoints" />.
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

        /// <summary>The distribution's sign-in client secret.</summary>
        public const string ClientSecret = "test-client-secret";

        /// <summary>The name of the session cookie's reference claim, which carries the session's key.</summary>
        public const string SessionKeyClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

        /// <summary>The key every test token is signed with.</summary>
        internal static readonly SymmetricSecurityKey SigningKey = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key" };

        private readonly WebApplication _app;

        private TellmaTestHost(WebApplication app, FakeCatalogReader catalog, FakeTimeProvider time)
        {
            _app = app;
            Catalog = catalog;
            Time = time;
            Client = app.GetTestClient();
            Rows = (InMemorySessionRows)app.Services.GetRequiredService<ISessionRows>();
            IdentityServer = app.Services.GetRequiredService<FakeIdentityServer>();
            Memberships = (FakeMembershipDirectory)app.Services.GetRequiredService<ITenantMembershipDirectory>();
        }

        /// <summary>A client of the host, at <c>https://localhost</c>.</summary>
        public HttpClient Client { get; }

        /// <summary>The host's services.</summary>
        public IServiceProvider Services => _app.Services;

        /// <summary>The in-memory catalog the registry reads.</summary>
        public FakeCatalogReader Catalog { get; }

        /// <summary>The host's clock.</summary>
        public FakeTimeProvider Time { get; }

        /// <summary>The catalog's session table.</summary>
        public InMemorySessionRows Rows { get; }

        /// <summary>The identity server's back channel.</summary>
        public FakeIdentityServer IdentityServer { get; }

        /// <summary>The membership hints.</summary>
        public FakeMembershipDirectory Memberships { get; }

        /// <summary>The session cookie's name.</summary>
        public string SessionCookieName => Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(TellmaAuthentication.SessionScheme).Cookie.Name!;

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
                ["Tellma:Identity:ClientSecret"] = ClientSecret,
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

            // --- What stands in for the catalog's sessions and memberships, and the reconciliation's lock ---
            builder.Services.Replace(ServiceDescriptor.Singleton<ISessionRows>(new InMemorySessionRows(time)));
            builder.Services.Replace(ServiceDescriptor.Singleton<ITenantMembershipDirectory>(new FakeMembershipDirectory()));
            builder.Services.Replace(ServiceDescriptor.Singleton<IMembershipReconcileStore>(new FakeReconcileStore()));

            // --- The identity server: a static issuer and key, so no discovery document is fetched ---
            _ = builder.Services.Configure<JwtBearerOptions>(TellmaAuthentication.BearerScheme, static bearer =>
            {
                OpenIdConnectConfiguration configuration = new() { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                bearer.Configuration = configuration;
            });
            FakeIdentityServer identityServer = new();
            _ = builder.Services.AddSingleton(identityServer);
            _ = builder.Services.Configure<OpenIdConnectOptions>(TellmaAuthentication.OidcScheme, oidc =>
            {
                oidc.Configuration = FakeIdentityServer.Configuration();
                oidc.BackchannelHttpHandler = identityServer;
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

        /// <summary>A session cookie for a signed-in user, its ticket stored as a sign-in stores it.</summary>
        /// <param name="subject">The user's subject.</param>
        /// <param name="acr">The authentication context class the session reached.</param>
        /// <param name="authTime">When the user signed in.</param>
        /// <param name="sessionId">The identity server's session.</param>
        /// <returns>The <c>Cookie</c> header's value.</returns>
        public string Cookie(string subject = "sub-1", string? acr = null, DateTimeOffset? authTime = null, string? sessionId = "sid-1")
        {
            return Session(subject, acr, authTime, sessionId).Cookie;
        }

        /// <summary>
        ///     A session for a signed-in user: its ticket stored in the session table with tokens that
        ///     need no refresh for an hour, and the cookie that references it.
        /// </summary>
        /// <param name="subject">The user's subject.</param>
        /// <param name="acr">The authentication context class the session reached.</param>
        /// <param name="authTime">When the user signed in.</param>
        /// <param name="sessionId">The identity server's session.</param>
        /// <param name="accessTokenLifetime">How long the access token lives; an hour by default.</param>
        /// <param name="refreshToken">The refresh token, if any.</param>
        /// <returns>The session.</returns>
        public TestSession Session(
            string subject = "sub-1",
            string? acr = null,
            DateTimeOffset? authTime = null,
            string? sessionId = "sid-1",
            TimeSpan? accessTokenLifetime = null,
            string? refreshToken = "refresh-1")
        {
            // --- The principal, as the sign-in keeps it ---
            List<Claim> claims = [new("sub", subject), new("azp", Slug), new("name", "Jane Doe"), new("email", "jane@example.com"), new("locale", "en")];
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

            // --- The properties, as the sign-in sets them: persistent, stamped, with the tokens ---
            DateTimeOffset now = Time.GetUtcNow();
            TellmaSessionOptions options = Services.GetRequiredService<IOptions<TellmaSessionOptions>>().Value;
            AuthenticationProperties properties = new() { IsPersistent = true, IssuedUtc = now };
            SessionProperties.SetCreatedAt(properties, now);
            properties.ExpiresUtc = SessionProperties.ExpiryAt(now, now, options.IdleLifetime, options.AbsoluteLifetime);
            List<AuthenticationToken> tokens =
            [
                new() { Name = "access_token", Value = "access-1" },
                new() { Name = "token_type", Value = "Bearer" },
                new() { Name = SessionProperties.ExpiresAtToken, Value = (now + (accessTokenLifetime ?? TimeSpan.FromHours(1))).ToString("o", CultureInfo.InvariantCulture) },
            ];
            if (refreshToken is not null)
            {
                tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
            }

            properties.StoreTokens(tokens);

            // --- The row, then the cookie that references it ---
            ClaimsPrincipal principal = new(new ClaimsIdentity(claims, TellmaAuthentication.SessionScheme, "sub", null));
            ICatalogSessionStore store = Services.GetRequiredService<ICatalogSessionStore>();
            string key = store.StoreAsync(new AuthenticationTicket(principal, properties, TellmaAuthentication.SessionScheme), new DefaultHttpContext(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return new TestSession(CookieFor(key), key, SessionKeys.HandleOf(key));
        }

        /// <summary>The cookie that references a stored session, as the cookie scheme protects it.</summary>
        /// <param name="key">The session's key.</param>
        /// <returns>The <c>Cookie</c> header's value.</returns>
        public string CookieFor(string key)
        {
            CookieAuthenticationOptions cookie = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(TellmaAuthentication.SessionScheme);
            ClaimsPrincipal reference = new(new ClaimsIdentity([new Claim(SessionKeyClaim, key)], cookie.ClaimsIssuer));
            string value = cookie.TicketDataFormat.Protect(new AuthenticationTicket(reference, new AuthenticationProperties(), TellmaAuthentication.SessionScheme));
            return $"{cookie.Cookie.Name}={value}";
        }

        /// <summary>A client of the host at <c>https://localhost</c>, as a browser reaches it over TLS.</summary>
        /// <returns>The client, which the caller disposes.</returns>
        public HttpClient CreateHttpsClient()
        {
            HttpClient client = _app.GetTestClient();
            client.BaseAddress = new Uri("https://localhost/");
            return client;
        }

        /// <summary>A bearer access token from the test issuer.</summary>
        /// <param name="claims">The token's claims.</param>
        /// <param name="type">The token's type header.</param>
        /// <param name="key">The signing key; the test key by default.</param>
        /// <returns>The token.</returns>
        public static string Token(IDictionary<string, object> claims, string type = "at+jwt", SecurityKey? key = null)
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
                SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.HmacSha256),
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
