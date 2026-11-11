// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Surfaces
{
    /// <summary>
    ///     The admin surface's first version, for the control plane: tenant listings, state changes,
    ///     registration and the membership hints. Every route sits under <c>/api/admin/v1</c> on the
    ///     control-plane policy.
    /// </summary>
    internal static class AdminEndpoints
    {
        /// <summary>The surface's first version's prefix.</summary>
        public const string V1Prefix = "/api/admin/v1";

        /// <summary>The actor every catalog write of the surface records, completed by the client id.</summary>
        public const string ActorPrefix = "control-plane:";

        /// <summary>Maps the first version.</summary>
        /// <param name="endpoints">The application's endpoints.</param>
        public static void MapV1(IEndpointRouteBuilder endpoints)
        {
            RouteGroupBuilder v1 = endpoints.MapGroup(V1Prefix);
            _ = v1.RequireAuthorization(TellmaPolicies.ControlPlane)
                .AsTenantlessEndpoint("admin")
                .WithMetadata(PlatformEndpointMetadata.Instance);

            _ = v1.MapGet("/info", InfoAsync);
            _ = v1.MapGet("/tenants", TenantsAsync);
            _ = v1.MapPost("/tenants", RegisterAsync);
            _ = v1.MapPost("/tenants/{id:int:min(1)}/state", SetStateAsync);
            _ = v1.MapGet("/tenants/{id:int:min(1)}/members", MembersAsync);
        }

        /// <summary><c>GET info</c>: the information document plus the tenant counts by state.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task InfoAsync(HttpContext context)
        {
            IReadOnlyList<TenantInfo> tenants = await Catalog(context).ListAsync(context.RequestAborted).ConfigureAwait(false);
            Dictionary<string, int> counts = Enum.GetValues<TenantState>().ToDictionary(
                static state => state.ToString(),
                state => tenants.Count(tenant => tenant.Descriptor.State == state),
                StringComparer.Ordinal);

            JsonObject document = ToObject(DistributionInfoEndpoint.Build(context.RequestServices));
            document["tenantCounts"] = JsonSerializer.SerializeToNode(counts, TellmaJson.Options);
            await WriteJsonAsync(context, StatusCodes.Status200OK, document).ConfigureAwait(false);
        }

        /// <summary><c>GET tenants</c>: every tenant's descriptor, without its location.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task TenantsAsync(HttpContext context)
        {
            IReadOnlyList<TenantInfo> tenants = await Catalog(context).ListAsync(context.RequestAborted).ConfigureAwait(false);
            TenantDescriptor[] descriptors = [.. tenants.Select(static tenant => tenant.Descriptor).OrderBy(static descriptor => descriptor.Id)];
            await WriteJsonAsync(context, StatusCodes.Status200OK, JsonSerializer.SerializeToNode(descriptors, TellmaJson.Options)).ConfigureAwait(false);
        }

        /// <summary><c>POST tenants/{id}/state</c>: an operator's state change, effective within one refresh.</summary>
        /// <param name="context">The request.</param>
        /// <param name="id">The tenant id.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task SetStateAsync(HttpContext context, int id)
        {
            StateChangeRequest? body = await ReadAsync<StateChangeRequest>(context).ConfigureAwait(false);
            if (body?.State is not TenantState state || state is TenantState.Frozen or TenantState.Provisioning)
            {
                // Frozen is the platform's own state, and Provisioning is entered by registration alone.
                await ProblemWriter.WriteAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "bad-request",
                    "The body names a state: Active, ReadOnly, Suspended or Retired.").ConfigureAwait(false);
                return;
            }

            TenantDescriptor descriptor = await Catalog(context)
                .SetStateAsync(id, state, body.Reason, Actor(context), context.RequestAborted)
                .ConfigureAwait(false);

            // Every instance serves the change once its snapshot next refreshes.
            TimeSpan refresh = context.RequestServices.GetRequiredService<IOptions<TellmaCatalogOptions>>().Value.RefreshInterval;
            JsonObject document = ToObject(descriptor);
            document["effectiveWithinSeconds"] = (int)Math.Ceiling(refresh.TotalSeconds);
            await WriteJsonAsync(context, StatusCodes.Status200OK, document).ConfigureAwait(false);
        }

        /// <summary>
        ///     <c>POST tenants</c>: registers a tenant at a sequence-allocated id and the default
        ///     placement, then starts its provisioning; refused, with nothing registered, where no
        ///     provisioning is configured.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task RegisterAsync(HttpContext context)
        {
            ITenantProvisioningTrigger trigger = context.RequestServices.GetRequiredService<ITenantProvisioningTrigger>();
            if (trigger is NotConfiguredProvisioningTrigger)
            {
                await ProblemWriter.WriteAsync(
                    context,
                    StatusCodes.Status501NotImplemented,
                    "provisioning-not-configured",
                    "This deployment provisions tenants through the migrator, not through the admin surface.").ConfigureAwait(false);
                return;
            }

            RegistrationRequest? body = await ReadAsync<RegistrationRequest>(context).ConfigureAwait(false);
            if (body is not { Name: { Length: > 0 } name, Category: TenantCategory category } || string.IsNullOrWhiteSpace(name))
            {
                await ProblemWriter.WriteAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "bad-request",
                    "The body names the tenant and its category.").ConfigureAwait(false);
                return;
            }

            TenantDescriptor descriptor = await Catalog(context)
                .RegisterAsync(new TenantRegistration(name, category, body.LiveTenantId, Location: null), Actor(context), requestedId: null, context.RequestAborted)
                .ConfigureAwait(false);
            await trigger.StartAsync(descriptor.Id, context.RequestAborted).ConfigureAwait(false);
            await WriteJsonAsync(context, StatusCodes.Status202Accepted, JsonSerializer.SerializeToNode(descriptor, TellmaJson.Options)).ConfigureAwait(false);
        }

        /// <summary><c>GET tenants/{id}/members</c>: the tenant's membership hints.</summary>
        /// <param name="context">The request.</param>
        /// <param name="id">The tenant id.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task MembersAsync(HttpContext context, int id)
        {
            IReadOnlyList<TenantInfo> tenants = await Catalog(context).ListAsync(context.RequestAborted).ConfigureAwait(false);
            if (!tenants.Any(tenant => tenant.Id == id))
            {
                throw new TenantNotFoundException(id);
            }

            IReadOnlyList<TenantMemberHint> members = await context.RequestServices
                .GetRequiredService<TenantMembershipDirectory>()
                .ListMembersAsync(id, context.RequestAborted)
                .ConfigureAwait(false);
            await WriteJsonAsync(context, StatusCodes.Status200OK, JsonSerializer.SerializeToNode(members, TellmaJson.Options)).ConfigureAwait(false);
        }

        /// <summary>The actor a catalog write records: the control plane's client.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The actor, within the catalog's length.</returns>
        internal static string Actor(HttpContext context)
        {
            string client = context.User.FindFirst(TellmaClaimTypes.AuthorizedParty)?.Value
                ?? context.User.FindFirst(TellmaClaimTypes.ClientId)?.Value
                ?? "unknown";
            string actor = ActorPrefix + client;
            return actor.Length <= TenantCatalog.MaxActorLength ? actor : actor[..TenantCatalog.MaxActorLength];
        }

        /// <summary>The tenant catalog's write side.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The catalog.</returns>
        private static ITenantCatalog Catalog(HttpContext context)
        {
            return context.RequestServices.GetRequiredService<ITenantCatalog>();
        }

        /// <summary>Reads a JSON body, or null when it is absent or malformed.</summary>
        /// <typeparam name="T">The body's type.</typeparam>
        /// <param name="context">The request.</param>
        /// <returns>The body, or null.</returns>
        private static async Task<T?> ReadAsync<T>(HttpContext context)
            where T : class
        {
            if (!context.Request.HasJsonContentType())
            {
                return null;
            }

            try
            {
                return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, TellmaJson.Options, context.RequestAborted).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>A value serialized as a JSON object, to add members to.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The object.</returns>
        private static JsonObject ToObject<T>(T value)
        {
            return JsonSerializer.SerializeToNode(value, TellmaJson.Options) as JsonObject
                ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{typeof(T).Name} did not serialize as an object."));
        }

        /// <summary>Writes a JSON answer.</summary>
        /// <param name="context">The request.</param>
        /// <param name="status">The status.</param>
        /// <param name="document">The body.</param>
        /// <returns>A task that completes when the body is written.</returns>
        private static Task WriteJsonAsync(HttpContext context, int status, JsonNode? document)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            return JsonSerializer.SerializeAsync(context.Response.Body, document, TellmaJson.Options, context.RequestAborted);
        }
    }

    /// <summary>The body of a state change.</summary>
    /// <param name="State">The state to move to.</param>
    /// <param name="Reason">Why, for the transition's record.</param>
    internal sealed record StateChangeRequest(TenantState? State, string? Reason);

    /// <summary>The body of a registration.</summary>
    /// <param name="Name">The tenant's name.</param>
    /// <param name="Category">Live or sandbox.</param>
    /// <param name="LiveTenantId">A sandbox's live tenant.</param>
    internal sealed record RegistrationRequest(string? Name, TenantCategory? Category, int? LiveTenantId);
}
