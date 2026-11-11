// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Globalization;
using System.Net.Http.Json;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>
    ///     Endpoints on every surface that answer the request context they ran under, so a test reads
    ///     what the pipeline bound.
    /// </summary>
    internal static class ProbeEndpoints
    {
        /// <summary>The authentication context class the step-up probes require.</summary>
        public const string StepUpAcr = "urn:tellma:acr:mfa";

        /// <summary>The age the step-up probes allow.</summary>
        public static readonly TimeSpan StepUpMaxAge = TimeSpan.FromMinutes(15);

        /// <summary>Maps the probes.</summary>
        /// <param name="app">The application.</param>
        /// <param name="endpoints">The platform's groups.</param>
        public static void Map(WebApplication app, TellmaEndpoints endpoints)
        {
            // --- The web surface: a read, a write, a write a read-only tenant allows, a step-up, an upload ---
            _ = endpoints.Web.MapGet("/probe", Probe).AllowMember();
            _ = endpoints.Web.MapPost("/probe", Probe).AllowMember();
            _ = endpoints.Web.MapPost("/tolerant", Probe)
                .WithMetadata(new TenantEndpointMetadata(TenantSurface.Web, IsMutation: true, AllowWhenReadOnly: true))
                .AllowMember();
            _ = endpoints.Web.MapPost("/stepup", Probe)
                .WithMetadata(new RequireAssuranceMetadata(StepUpAcr, StepUpMaxAge))
                .AllowMember();
            _ = endpoints.Web.MapPost("/upload", Probe).AcceptsBinary(1024).AllowMember();

            // --- The hub's negotiate, the blobs, the public API ---
            _ = endpoints.Hub.MapPost("/negotiate", Probe).AllowMember();
            _ = endpoints.Blobs.MapGet("/{kind}/{id:int}", Probe).AllowMember();
            _ = endpoints.Api.MapGet("/probe", Probe).AllowMember();
            _ = endpoints.Api.MapPost("/probe", Probe).AllowMember();
            _ = endpoints.Api.MapPost("/stepup", Probe)
                .WithMetadata(new RequireAssuranceMetadata(StepUpAcr, StepUpMaxAge))
                .AllowMember();

            // --- MCP, mapped by hand as its feature maps it ---
            _ = app.MapGet("/{tenantId:int:min(1)}/mcp", Probe)
                .RequireAuthorization(TellmaPolicies.Mcp)
                .WithMetadata(new TenantEndpointMetadata(TenantSurface.Mcp, IsMutation: false, AllowWhenReadOnly: false), PlatformEndpointMetadata.Instance)
                .AllowMember();

            // --- Tenantless: one on the session fallback, one anonymous ---
            _ = endpoints.Tenantless.MapPost("/echo", Probe).AsTenantlessEndpoint("test");
            _ = endpoints.Tenantless.MapPost("/open", Probe).AllowAnonymous().AsTenantlessEndpoint("test");
        }

        /// <summary>Answers the request context.</summary>
        /// <param name="accessor">The request's context.</param>
        /// <returns>The context as JSON.</returns>
        private static IResult Probe(IRequestContextAccessor accessor)
        {
            RequestContext context = accessor.Current;
            return Results.Json(new ProbeResult(
                context.Tenant?.Id,
                context.UserId,
                context.Kind.ToString(),
                context.Subject,
                context.ClientId,
                context.SessionId,
                context.SessionHandle,
                context.Client,
                context.Assurance?.Acr,
                CultureInfo.CurrentCulture.Name));
        }
    }

    /// <summary>What a probe answers.</summary>
    /// <param name="TenantId">The bound tenant.</param>
    /// <param name="UserId">The bound user.</param>
    /// <param name="Kind">The principal's kind.</param>
    /// <param name="Subject">The principal's subject.</param>
    /// <param name="ClientId">The credential's client.</param>
    /// <param name="SessionId">The identity server's session.</param>
    /// <param name="SessionHandle">The cookie session's handle.</param>
    /// <param name="Client">The request's client name.</param>
    /// <param name="Acr">The session's authentication context class.</param>
    /// <param name="Culture">The culture the handler ran under.</param>
    internal sealed record ProbeResult(
        int? TenantId,
        int? UserId,
        string Kind,
        string? Subject,
        string? ClientId,
        string? SessionId,
        string? SessionHandle,
        string Client,
        string? Acr,
        string Culture)
    {
        /// <summary>Reads a probe's answer.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The answer.</returns>
        public static async Task<ProbeResult> ReadAsync(HttpResponseMessage response)
        {
            ArgumentNullException.ThrowIfNull(response);
            return await response.Content.ReadFromJsonAsync<ProbeResult>(TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The probe answered nothing.");
        }
    }
}
