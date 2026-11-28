// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Surfaces
{
    /// <summary>
    ///     The RFC 9728 protected-resource metadata of a tenant's MCP endpoint, built from the public
    ///     origin and the configured display name, never from the request's <c>Host</c>. It answers
    ///     for any tenant id, resolving none, so it reveals nothing about which tenants exist: the
    ///     resource is named by the distribution alone, never by the tenant.
    /// </summary>
    internal static class ProtectedResourceEndpoint
    {
        /// <summary>The document's route.</summary>
        public const string Route = "/.well-known/oauth-protected-resource/{tenantId:int:min(1)}/mcp";

        /// <summary>The document's cache lifetime.</summary>
        public const string CacheControl = "public, max-age=300";

        /// <summary>The snake_case serializer of the document, whose member names RFC 9728 fixes.</summary>
        private static readonly JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        /// <summary>Answers the document.</summary>
        /// <param name="context">The request.</param>
        /// <param name="tenantId">The tenant id the route names.</param>
        /// <returns>A task that completes when the document is written.</returns>
        public static Task WriteAsync(HttpContext context, int tenantId)
        {
            ArgumentNullException.ThrowIfNull(context);
            IServiceProvider services = context.RequestServices;
            string origin = services.GetRequiredService<IConfiguration>()[HostConfigurationKeys.PublicOrigin]
                ?? throw new InvalidOperationException($"{HostConfigurationKeys.PublicOrigin} is not set.");
            IdentityHostOptions identity = services.GetRequiredService<IOptions<IdentityHostOptions>>().Value;
            DistributionOptions distribution = services.GetRequiredService<IOptions<DistributionOptions>>().Value;

            // The startup gate has checked the display name; the tenant's own name stays out of an
            // anonymous document, which would otherwise tell a stranger which tenant ids exist.
            ProtectedResourceDocument document = new(
                Resource(origin, tenantId),
                [identity.ResolveAuthority(origin)],
                [TellmaClaimTypes.ApiScope],
                ["header"],
                distribution.DisplayName ?? throw new InvalidOperationException($"{DistributionOptions.SectionPath}:{nameof(DistributionOptions.DisplayName)} is not set."));
            context.Response.Headers[HeaderNames.CacheControl] = CacheControl;
            context.Response.ContentType = "application/json; charset=utf-8";
            return JsonSerializer.SerializeAsync(context.Response.Body, document, Options, context.RequestAborted);
        }

        /// <summary>The RFC 8707 resource identifier of a tenant's MCP endpoint: the audience its tokens carry.</summary>
        /// <param name="origin">The public origin.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The identifier, with no trailing slash.</returns>
        public static string Resource(string origin, int tenantId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(origin);
            return string.Create(CultureInfo.InvariantCulture, $"{origin}/{tenantId}/mcp");
        }

        /// <summary>The URL of a tenant's metadata document, by RFC 9728's path insertion into its resource.</summary>
        /// <param name="origin">The public origin.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The URL.</returns>
        public static string Url(string origin, int tenantId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(origin);
            return string.Create(CultureInfo.InvariantCulture, $"{origin}/.well-known/oauth-protected-resource/{tenantId}/mcp");
        }
    }

    /// <summary>An RFC 9728 protected-resource metadata document.</summary>
    /// <param name="Resource">The resource identifier: the tenant's MCP endpoint.</param>
    /// <param name="AuthorizationServers">The issuers whose tokens the resource accepts.</param>
    /// <param name="ScopesSupported">The scopes a token for the resource carries.</param>
    /// <param name="BearerMethodsSupported">How a token is presented.</param>
    /// <param name="ResourceName">The resource's human-readable name, which a client shows when it asks for consent.</param>
    internal sealed record ProtectedResourceDocument(
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string> AuthorizationServers,
        [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
        [property: JsonPropertyName("bearer_methods_supported")] IReadOnlyList<string> BearerMethodsSupported,
        [property: JsonPropertyName("resource_name")] string ResourceName);
}
