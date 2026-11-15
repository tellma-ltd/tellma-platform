// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     An in-memory stand-in for the hosts that publish metadata documents: each URL answers
    ///     with what a test configured, and every request is recorded. Nothing leaves the process.
    /// </summary>
    public sealed class FakeMetadataServer : HttpMessageHandler
    {
        /// <summary>The configured answers, by absolute URL.</summary>
        private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);

        /// <summary>Every URL requested, in order.</summary>
        public ConcurrentQueue<Uri> Requests { get; } = new();

        /// <summary>Answers a URL with a JSON body.</summary>
        /// <param name="url">The URL.</param>
        /// <param name="json">The body.</param>
        /// <param name="contentType">The media type.</param>
        /// <param name="maxAge">The <c>Cache-Control: max-age</c>, when the response sets one.</param>
        public void Serve(string url, string json, string contentType = "application/json", TimeSpan? maxAge = null)
        {
            Respond(url, () =>
            {
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, contentType),
                };
                if (maxAge is { } age)
                {
                    response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = age };
                }

                return response;
            });
        }

        /// <summary>Answers a URL with a response built on each request.</summary>
        /// <param name="url">The URL.</param>
        /// <param name="response">Builds the response.</param>
        public void Respond(string url, Func<HttpResponseMessage> response)
        {
            ArgumentNullException.ThrowIfNull(response);
            _routes[url] = (_, _) => Task.FromResult(response());
        }

        /// <summary>Answers a URL by running a handler, which may wait or throw.</summary>
        /// <param name="url">The URL.</param>
        /// <param name="handler">The handler.</param>
        public void Handle(string url, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _routes[url] = handler;
        }

        /// <summary>Makes a URL fail as an unreachable host does.</summary>
        /// <param name="url">The URL.</param>
        public void Fail(string url)
        {
            _routes[url] = (_, _) => throw new HttpRequestException("The host is unreachable.");
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!);
            return _routes.TryGetValue(request.RequestUri!.AbsoluteUri, out Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? route)
                ? route(request, cancellationToken)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>Hands every named client the same handler, recording which names were asked for.</summary>
    /// <param name="handler">The handler every client sends through.</param>
    public sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <summary>The client names asked for.</summary>
        public ConcurrentQueue<string> Names { get; } = new();

        /// <inheritdoc />
        public HttpClient CreateClient(string name)
        {
            Names.Enqueue(name);
            return new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        }
    }

    /// <summary>Records audit rows instead of writing them.</summary>
    public sealed class AuditRecorder : IAuditLogger
    {
        /// <summary>Every row written.</summary>
        public ConcurrentQueue<AuditEventEntry> Entries { get; } = new();

        /// <inheritdoc />
        public Task LogAsync(AuditEventEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Enqueue(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     The resolver over fakes: a fake server, a fake clock, a recording audit log and a live
    ///     meter, with the engine's own fetcher, rate limit and cache in between.
    /// </summary>
    public sealed class ResolverRig : IDisposable
    {
        /// <summary>The service provider the auditor's scopes come from.</summary>
        private readonly ServiceProvider _provider;

        /// <summary>Builds the rig.</summary>
        /// <param name="configure">Adjusts the options before anything reads them.</param>
        public ResolverRig(Action<TellmaIdentityClientMetadataOptions>? configure = null)
        {
            configure?.Invoke(Options.ClientMetadata);

            ServiceCollection services = new();
            services.AddMetrics();
            services.AddSingleton(Audit);
            services.AddScoped<IAuditLogger>(static provider => provider.GetRequiredService<AuditRecorder>());
            _provider = services.BuildServiceProvider();

            IMeterFactory meterFactory = _provider.GetRequiredService<IMeterFactory>();
            Fetches = new Microsoft.Extensions.Diagnostics.Metrics.Testing.MetricCollector<long>(
                meterFactory, IdentityMetrics.MeterName, IdentityMetrics.ClientMetadataFetchesInstrumentName);

            Microsoft.Extensions.Options.IOptions<TellmaIdentityOptions> options = Microsoft.Extensions.Options.Options.Create(Options);
            HttpClients = new FixedHttpClientFactory(Server);
            RateLimiter = new ClientMetadataRateLimiter(options, Time);
            Fetcher = new ClientMetadataFetcher(HttpClients, RateLimiter, options, Time);
            Cache = new ClientMetadataCache(options);
            Resolver = new ClientMetadataDocumentResolver(
                Fetcher,
                Cache,
                new ClientMetadataAuditor(_provider.GetRequiredService<IServiceScopeFactory>()),
                new IdentityMetrics(meterFactory),
                options,
                Time,
                new HttpContextAccessor());
        }

        /// <summary>The engine options.</summary>
        public TellmaIdentityOptions Options { get; } = new();

        /// <summary>The fake publishing hosts.</summary>
        public FakeMetadataServer Server { get; } = new();

        /// <summary>The client factory over <see cref="Server" />.</summary>
        public FixedHttpClientFactory HttpClients { get; }

        /// <summary>The clock.</summary>
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));

        /// <summary>The audit rows written.</summary>
        public AuditRecorder Audit { get; } = new();

        /// <summary>The fetch counter's measurements.</summary>
        public Microsoft.Extensions.Diagnostics.Metrics.Testing.MetricCollector<long> Fetches { get; }

        /// <summary>The per-host limit.</summary>
        public ClientMetadataRateLimiter RateLimiter { get; }

        /// <summary>The guarded fetch.</summary>
        public ClientMetadataFetcher Fetcher { get; }

        /// <summary>The document cache.</summary>
        public ClientMetadataCache Cache { get; }

        /// <summary>The resolver under test.</summary>
        public ClientMetadataDocumentResolver Resolver { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            Fetches.Dispose();
            _provider.Dispose();
            Server.Dispose();
        }
    }

    /// <summary>Builds documents and keys for the tests.</summary>
    public static class MetadataDocuments
    {
        /// <summary>A document client's id.</summary>
        public const string ClientId = "https://agent.example.com/oauth/client.json";

        /// <summary>Its loopback redirect URI.</summary>
        public const string LoopbackRedirect = "http://127.0.0.1/callback";

        /// <summary>Its key-set URL, on its own origin.</summary>
        public const string JwksUri = "https://agent.example.com/oauth/jwks.json";

        /// <summary>A valid public document for <paramref name="clientId" />.</summary>
        /// <param name="clientId">The client id.</param>
        /// <returns>The document, for a test to change before serializing.</returns>
        public static JsonObject Public(string clientId = ClientId)
        {
            return new JsonObject
            {
                ["client_id"] = clientId,
                ["client_name"] = "Example Agent",
                ["redirect_uris"] = new JsonArray(LoopbackRedirect),
                ["token_endpoint_auth_method"] = "none",
                ["grant_types"] = new JsonArray("authorization_code", "refresh_token"),
            };
        }

        /// <summary>A valid <c>private_key_jwt</c> document naming <see cref="JwksUri" />.</summary>
        /// <returns>The document.</returns>
        public static JsonObject PrivateKeyJwt()
        {
            JsonObject document = Public();
            document["token_endpoint_auth_method"] = "private_key_jwt";
            document["jwks_uri"] = JwksUri;
            return document;
        }

        /// <summary>A public RSA signing key as a JWK object.</summary>
        /// <param name="bits">The modulus size.</param>
        /// <param name="kid">The key id.</param>
        /// <returns>The JWK.</returns>
        public static JsonObject RsaKey(int bits = 2048, string kid = "k1")
        {
            using var rsa = RSA.Create(bits);
            RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: false);
            return new JsonObject
            {
                ["kty"] = "RSA",
                ["kid"] = kid,
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
            };
        }

        /// <summary>A key set holding the given keys.</summary>
        /// <param name="keys">The keys.</param>
        /// <returns>The key set's JSON.</returns>
        public static string KeySet(params JsonObject[] keys)
        {
            return new JsonObject { ["keys"] = new JsonArray([.. keys]) }.ToJsonString();
        }
    }
}
