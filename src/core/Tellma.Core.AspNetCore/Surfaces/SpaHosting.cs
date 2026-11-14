// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Surfaces
{
    /// <summary>Where the SPA's published assets are described; a seam the host's tests set.</summary>
    internal sealed class SpaHostingOptions
    {
        /// <summary>
        ///     The static web assets manifest; null for the one the SDK publishes beside the
        ///     application, <c>{ApplicationName}.staticwebassets.endpoints.json</c>.
        /// </summary>
        public string? ManifestPath { get; set; }
    }

    /// <summary>
    ///     The SPA's hosting: its published files, endpoint-routed with cache headers by their hashed
    ///     names, and the navigation fallback that answers a deep link with <c>index.html</c> and every
    ///     other unmatched request with a problem.
    /// </summary>
    internal static partial class SpaHosting
    {
        /// <summary>The SPA's shell.</summary>
        public const string IndexFile = "index.html";

        /// <summary>The cache lifetime of a hashed file: a year, never revalidated.</summary>
        public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

        /// <summary>The cache rule of every unhashed file: always revalidated against its ETag.</summary>
        public const string NoCache = "no-cache";

        /// <summary>The fetch metadata header naming the request's mode.</summary>
        private const string SecFetchModeHeader = "Sec-Fetch-Mode";

        /// <summary>The fallback's route: every path.</summary>
        public const string FallbackRoute = "{*path}";

        /// <summary>The response headers of <c>index.html</c> beside its content security policy.</summary>
        internal static readonly KeyValuePair<string, string>[] IndexHeaders =
        [
            new(HeaderNames.CacheControl, NoCache),
            new("Referrer-Policy", "strict-origin-when-cross-origin"),
            new(HeaderNames.XContentTypeOptions, "nosniff"),
            new("Cross-Origin-Opener-Policy", "same-origin"),
            new("Permissions-Policy", "camera=(), microphone=(), geolocation=(), display-capture=(self)"),
        ];

        /// <summary>Maps the SPA's files, when published, and the navigation fallback.</summary>
        /// <param name="app">The application.</param>
        public static void Map(WebApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            string policy = ContentSecurityPolicy(app.Configuration, HostSettings.Read<IdentityHostOptions>(app.Configuration, IdentityHostOptions.SectionPath));

            // --- The published files, when this host carries the SPA ---
            string manifest = ManifestPath(app);
            if (File.Exists(manifest))
            {
                // Mapped inside a group without a prefix, so a stamp reaches every endpoint the call
                // maps. A build's manifest (dotnet run, a test host) also maps a development
                // endpoint, {**path:file}, that serves files from disk and that the call's own
                // convention builder never sees; unstamped, it would fail the endpoint audit.
                RouteGroupBuilder group = app.MapGroup(string.Empty);
                StaticAssetsEndpointConventionBuilder assets = group.MapStaticAssets(manifest);
                assets.Add(endpoint => StampAsset(endpoint, policy));
                ((IEndpointConventionBuilder)group).Add(StampMetadata);
            }
            else
            {
                ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SpaHosting));
                HostLog.SpaAssetsMissing(logger, manifest);
            }

            // --- The fallback: index.html for a navigation, a problem for anything else ---
            RequestDelegate index = IndexDelegate(app, policy);
            _ = app.MapFallback(FallbackRoute, context => FallbackAsync(context, index))
                .AllowAnonymous()
                .AsTenantlessEndpoint("spa-fallback")
                .WithMetadata(PlatformEndpointMetadata.Instance);
        }

        /// <summary>Whether a file name is hashed: a <c>-</c> and at least eight <c>[A-Z0-9]</c> before its extension.</summary>
        /// <param name="route">The file's route; a compressed variant's suffix is ignored.</param>
        /// <returns>True when it is.</returns>
        public static bool IsHashed(string route)
        {
            ArgumentNullException.ThrowIfNull(route);
            return HashedName().IsMatch(WithoutCompression(route[(route.LastIndexOf('/') + 1)..]));
        }

        /// <summary>A file name without its <c>.br</c> or <c>.gz</c> suffix.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The name of the uncompressed file.</returns>
        private static string WithoutCompression(string name)
        {
            return name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
                ? name[..^3]
                : name;
        }

        /// <summary>
        ///     Whether a static asset endpoint serves the SPA's shell. A publish maps the shell under
        ///     several routes (<c>index.html</c>, its compressed files' names and a fingerprinted name),
        ///     and each route's compressed variants are rows of their own with the same route and a
        ///     compressed file; every one of them is the shell.
        /// </summary>
        /// <param name="route">The endpoint's route.</param>
        /// <param name="assetFile">The file it serves, compressed or not.</param>
        /// <returns>True when the endpoint answers with <c>index.html</c>'s content.</returns>
        public static bool IsIndexAsset(string route, string assetFile)
        {
            ArgumentNullException.ThrowIfNull(route);
            ArgumentNullException.ThrowIfNull(assetFile);
            return string.Equals(route, IndexFile, StringComparison.OrdinalIgnoreCase)
                || string.Equals(WithoutCompression(assetFile), IndexFile, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether the fallback answers a request with <c>index.html</c>.</summary>
        /// <param name="request">The request.</param>
        /// <returns>True for a navigation to a path the SPA owns.</returns>
        public static bool ServesIndex(HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                return false;
            }

            // --- A navigation: the browser says so, or (an older browser) the request accepts HTML ---
            bool navigation = request.Headers.TryGetValue(SecFetchModeHeader, out StringValues mode)
                ? mode.Count == 1 && string.Equals(mode[0], "navigate", StringComparison.Ordinal)
                : AcceptsHtml(request);
            if (!navigation)
            {
                return false;
            }

            // --- A path the SPA owns: no platform word first, no tenant surface word second ---
            string[] segments = Segments(request.Path);
            return segments.Length == 0
                || (!IsReserved(TellmaRoutes.ServerFirstSegments, segments[0])
                    && !(IsTenantId(segments[0]) && segments.Length > 1 && IsReserved(TellmaRoutes.ServerTenantSegments, segments[1])));
        }

        /// <summary>The content security policy of <c>index.html</c>.</summary>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="identity">The identity options.</param>
        /// <returns>The policy.</returns>
        public static string ContentSecurityPolicy(IConfiguration configuration, IdentityHostOptions identity)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(identity);

            // --- The variations: the realtime service's origins, the standalone authority's ---
            string connect = "'self'";
            if (SignalROrigins(configuration[HostConfigurationKeys.AzureSignalRConnectionString]) is (string https, string wss))
            {
                connect += $" {https} {wss}";
            }

            string form = "'self'";
            if (identity.Mode == IdentityMode.Standalone
                && Uri.TryCreate(identity.Authority, UriKind.Absolute, out Uri? authority))
            {
                form += " " + authority.GetLeftPart(UriPartial.Authority);
            }

            return string.Join(
                "; ",
                "default-src 'self'",
                "script-src 'self'",
                "style-src 'self' 'unsafe-inline'",
                "img-src 'self' data: blob:",
                "font-src 'self'",
                $"connect-src {connect}",
                "frame-src 'self' blob:",
                "frame-ancestors 'none'",
                $"form-action {form}",
                "base-uri 'self'",
                "object-src 'none'",
                "worker-src 'self'",
                "manifest-src 'self'",
                "require-trusted-types-for 'script'",
                "trusted-types angular angular#bundler tellma");
        }

        /// <summary>The https and wss origins of an Azure SignalR connection string's endpoint.</summary>
        /// <param name="connectionString">The connection string, or null.</param>
        /// <returns>The two origins, or null when no endpoint is configured.</returns>
        internal static (string Https, string Wss)? SignalROrigins(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return null;
            }

            // Only the endpoint is read; the access key never leaves the connection string.
            foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int equals = part.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0
                    && string.Equals(part[..equals].Trim(), "Endpoint", StringComparison.OrdinalIgnoreCase)
                    && Uri.TryCreate(part[(equals + 1)..].Trim(), UriKind.Absolute, out Uri? endpoint)
                    && (endpoint.Scheme == Uri.UriSchemeHttps || endpoint.Scheme == Uri.UriSchemeHttp))
                {
                    string socket = endpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
                    return (endpoint.GetLeftPart(UriPartial.Authority), $"{socket}://{endpoint.Authority}");
                }
            }

            return null;
        }

        /// <summary>
        ///     Marks a static file's endpoint anonymous, tenantless and the platform's, once: both the
        ///     assets' own conventions and their group's reach an asset's endpoint, and only the
        ///     group's reaches the development endpoint that serves files from disk.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        private static void StampMetadata(EndpointBuilder endpoint)
        {
            if (endpoint.Metadata.OfType<TenantlessEndpointMetadata>().Any())
            {
                return;
            }

            endpoint.Metadata.Add(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute());
            endpoint.Metadata.Add(new TenantlessEndpointMetadata("spa-assets"));
            endpoint.Metadata.Add(PlatformEndpointMetadata.Instance);
        }

        /// <summary>Stamps a static asset's endpoint and sets its response headers.</summary>
        /// <param name="endpoint">The asset's endpoint.</param>
        /// <param name="policy">The content security policy of <c>index.html</c>.</param>
        private static void StampAsset(EndpointBuilder endpoint, string policy)
        {
            StampMetadata(endpoint);

            StaticAssetDescriptor? asset = endpoint.Metadata.OfType<StaticAssetDescriptor>().LastOrDefault();
            RequestDelegate? inner = endpoint.RequestDelegate;
            if (asset is null || inner is null)
            {
                return;
            }

            // The asset's handler writes the manifest's headers; these replace its cache rule, and
            // index.html's add its security headers, just before the response starts.
            bool isIndex = IsIndexAsset(asset.Route, asset.AssetPath);
            string cacheControl = IsHashed(asset.Route) ? ImmutableCacheControl : NoCache;
            endpoint.RequestDelegate = context =>
            {
                context.Response.OnStarting(() =>
                {
                    if (isIndex)
                    {
                        ApplyIndexHeaders(context.Response, policy);
                    }
                    else
                    {
                        context.Response.Headers[HeaderNames.CacheControl] = cacheControl;
                    }

                    return Task.CompletedTask;
                });
                return inner(context);
            };
        }

        /// <summary>Sets <c>index.html</c>'s headers.</summary>
        /// <param name="response">The response.</param>
        /// <param name="policy">The content security policy.</param>
        private static void ApplyIndexHeaders(HttpResponse response, string policy)
        {
            foreach ((string name, string value) in IndexHeaders)
            {
                response.Headers[name] = value;
            }

            response.Headers[HeaderNames.ContentSecurityPolicy] = policy;
        }

        /// <summary>The fallback: <c>index.html</c> for a navigation, a problem for anything else.</summary>
        /// <param name="context">The request.</param>
        /// <param name="index">The delegate that serves <c>index.html</c>.</param>
        /// <returns>A task that completes when the request is answered.</returns>
        private static Task FallbackAsync(HttpContext context, RequestDelegate index)
        {
            if (ServesIndex(context.Request))
            {
                return index(context);
            }

            // Under a tenant id, the path names a tenant route nothing matched.
            string[] segments = Segments(context.Request.Path);
            string code = segments.Length > 0 && IsTenantId(segments[0]) ? "route-not-found" : "not-found";
            return ProblemWriter.WriteAsync(context, StatusCodes.Status404NotFound, code);
        }

        /// <summary>
        ///     The delegate that serves <c>index.html</c> from the web root with its ETag and headers, as
        ///     a file request would be served, answering a problem when the host carries no SPA.
        /// </summary>
        /// <param name="app">The application.</param>
        /// <param name="policy">The content security policy.</param>
        /// <returns>The delegate.</returns>
        private static RequestDelegate IndexDelegate(WebApplication app, string policy)
        {
            IApplicationBuilder pipeline = ((IApplicationBuilder)app).New();
            _ = pipeline.Use(next => async context =>
            {
                // The static file middleware serves only a request no endpoint claimed. The request's
                // own path and endpoint come back afterwards, so the request log, the traces and the
                // metrics name the deep link the browser opened, not the shell.
                PathString path = context.Request.Path;
                Endpoint? endpoint = context.GetEndpoint();
                context.Request.Path = "/" + IndexFile;
                context.SetEndpoint(null);
                try
                {
                    await next(context).ConfigureAwait(false);
                }
                finally
                {
                    context.Request.Path = path;
                    context.SetEndpoint(endpoint);
                }
            });
            _ = pipeline.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = file => ApplyIndexHeaders(file.Context.Response, policy),
            });
            pipeline.Run(static context => ProblemWriter.WriteAsync(context, StatusCodes.Status404NotFound, "not-found"));
            return pipeline.Build();
        }

        /// <summary>The static web assets manifest's path.</summary>
        /// <param name="app">The application.</param>
        /// <returns>The path.</returns>
        private static string ManifestPath(WebApplication app)
        {
            string? configured = app.Services.GetService<IOptions<SpaHostingOptions>>()?.Value.ManifestPath;
            string name = configured ?? (app.Services.GetRequiredService<IWebHostEnvironment>().ApplicationName + ".staticwebassets.endpoints.json");
            return Path.IsPathRooted(name) ? name : Path.Combine(AppContext.BaseDirectory, name);
        }

        /// <summary>Whether a request accepts HTML.</summary>
        /// <param name="request">The request.</param>
        /// <returns>True when its <c>Accept</c> names <c>text/html</c>.</returns>
        private static bool AcceptsHtml(HttpRequest request)
        {
            return MediaTypeHeaderValue.TryParseList(request.Headers.Accept, out IList<MediaTypeHeaderValue>? types)
                && types.Any(static type => type.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A path's segments.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The non-empty segments.</returns>
        private static string[] Segments(PathString path)
        {
            return (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Whether a segment is a word of a reserved set, ignoring case as routing does.</summary>
        /// <param name="words">The reserved words.</param>
        /// <param name="segment">The segment.</param>
        /// <returns>True when it is.</returns>
        private static bool IsReserved(IReadOnlyList<string> words, string segment)
        {
            return words.Contains(segment, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Whether a segment is a tenant id: an integer, as the tenant routes' constraint reads one.</summary>
        /// <param name="segment">The segment.</param>
        /// <returns>True when it is.</returns>
        private static bool IsTenantId(string segment)
        {
            return int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _);
        }

        /// <summary>A hashed file name: a hyphen and eight or more uppercase letters or digits before the extension.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex("-[A-Z0-9]{8,}\\.[^.]+$", RegexOptions.CultureInvariant)]
        private static partial Regex HashedName();
    }
}
