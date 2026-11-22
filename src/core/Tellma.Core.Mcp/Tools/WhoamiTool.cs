// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Mcp.Hosting;

namespace Tellma.Core.Mcp.Tools
{
    /// <summary>
    ///     <c>tellma_whoami</c>: the caller, the tenant and its category, what the caller may do on
    ///     each entity, and the version tags their view was read at. The only tool that declares an
    ///     output schema, so it answers its result both as structured content and as text.
    /// </summary>
    /// <remarks>
    ///     One read round trip connects the caller afresh, so a deactivated member is refused here
    ///     even when the connect cache still holds them; the permission matrix is the evaluator's
    ///     over every registered securable, sensitive ones left out.
    /// </remarks>
    internal sealed class WhoamiTool : McpTool
    {
        /// <summary>The tool's description, written for the model.</summary>
        public const string ToolDescription =
            "Tells who you are in this tenant and what you may do: your user, the tenant with its category (Live or Sandbox: a sandbox is a copy, and nothing done there reaches the live tenant), "
            + "its languages, calendars and time zone, and for each entity the operations and actions you are granted, with those granted only on some rows listed under filtered. "
            + "Call it first. Permissions are checked again on every call, whatever this answers.";

        /// <summary>The standard operations, as the matrix names them, in order.</summary>
        private static readonly string[] StandardOperations = [AccessActions.Read, AccessActions.Save, AccessActions.Delete, AccessActions.Activate];

        /// <summary>Defines the tool.</summary>
        public WhoamiTool()
            : base(McpToolNames.Whoami, "Who am I", ToolDescription, ToolSchema.Input([], []), idempotent: true, OutputSchema)
        {
        }

        /// <summary>The schema of the tool's structured content.</summary>
        internal static JsonElement OutputSchema { get; } = BuildOutputSchema();

        /// <inheritdoc />
        protected override async Task<CallToolResult> InvokeAsync(ToolArguments arguments, IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- One read round trip: the caller connected afresh, and the tags it read ---
            (ConnectedUser user, BatchOutcome outcome) = await services.GetRequiredService<IGuardedBatchRunner>().RunAsync(
                BatchPurpose.Read,
                static (_, _) => ValueTask.CompletedTask,
                static (connected, outcome) => (connected, outcome),
                cancellationToken).ConfigureAwait(false);

            // --- The matrix, over every registered securable ---
            IReadOnlyList<AccessDecision> decisions = await services.GetRequiredService<IAccessEvaluator>().EvaluateAllAsync(cancellationToken).ConfigureAwait(false);
            RequestContext context = services.GetRequiredService<IRequestContextAccessor>().Current;
            WhoamiResult result = Build(
                context,
                user,
                decisions,
                services.GetRequiredService<McpEntities>(),
                services.GetRequiredService<ISecurableRegistry>(),
                VersionTagWire.Items(outcome.VersionTags, outcome.UserVersionTags));

            // --- The result twice: structured, and as the text a client without structured content reads ---
            JsonElement structured = JsonSerializer.SerializeToElement(result, TellmaJson.Options);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = structured.GetRawText() }],
                StructuredContent = structured,
            };
        }

        /// <summary>Builds the result from the request's context and the round trip's answers.</summary>
        /// <param name="context">The request's completed context.</param>
        /// <param name="user">The caller, connected.</param>
        /// <param name="decisions">The evaluator's decisions over every securable.</param>
        /// <param name="entities">The entities the tools reach.</param>
        /// <param name="securables">The securable registry, which marks the sensitive ones.</param>
        /// <param name="tags">The version tags the round trip read.</param>
        /// <returns>The result.</returns>
        internal static WhoamiResult Build(
            RequestContext context,
            ConnectedUser user,
            IReadOnlyList<AccessDecision> decisions,
            McpEntities entities,
            ISecurableRegistry securables,
            IReadOnlyList<KeyValuePair<string, string>> tags)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(decisions);
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(securables);
            ArgumentNullException.ThrowIfNull(tags);
            TenantDescriptor tenant = context.RequireTenant();
            TenantSettings settings = context.TenantSettings
                ?? throw new InvalidOperationException("The request's context carries no tenant settings after its front door admitted it.");

            // --- The caller, named in their content language ---
            int index = context.ContentLanguageIndex;
            UserProfile profile = user.Profile;
            string name = (index switch { 2 => profile.Name2, 3 => profile.Name3, _ => null }) is { Length: > 0 } localized ? localized : profile.Name;
            IReadOnlyList<string> calendars = [.. settings.Calendars.Select(static calendar => calendar.Code)];
            string calendar = profile.PreferredCalendar is string preferred && calendars.Contains(preferred, StringComparer.Ordinal) ? preferred : calendars[0];
            WhoamiUser caller = new(
                user.UserId,
                name,
                profile.Email,
                settings.Languages[Math.Min(index, settings.Languages.Count) - 1].Code,
                calendar,
                context.TimeZone);

            // --- The tenant ---
            WhoamiTenant serves = new(
                tenant.Id,
                McpServerIdentity.TenantName(context, tenant),
                tenant.Category,
                [.. settings.Languages.Select(static language => language.Code)],
                calendars,
                context.TenantTimeZone);

            return new WhoamiResult(caller, serves, Matrix(decisions, entities, securables), tags.ToDictionary(StringComparer.Ordinal));
        }

        /// <summary>The permission matrix: each reachable entity's granted operations and actions, sensitive ones left out.</summary>
        /// <param name="decisions">The evaluator's decisions.</param>
        /// <param name="entities">The entities the tools reach.</param>
        /// <param name="securables">The securable registry.</param>
        /// <returns>The matrix, an entry per entity with any grant, in the registry's order.</returns>
        internal static IReadOnlyList<WhoamiEntityAccess> Matrix(IReadOnlyList<AccessDecision> decisions, McpEntities entities, ISecurableRegistry securables)
        {
            ArgumentNullException.ThrowIfNull(decisions);
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(securables);

            // --- The allowed decisions on the entities the tools reach, by entity ---
            Dictionary<string, List<AccessDecision>> byEntity = new(StringComparer.Ordinal);
            foreach (AccessDecision decision in decisions)
            {
                if (!decision.IsAllowed
                    || entities.Find(decision.Resource) is not McpEntity entity
                    || securables.Find(decision.Resource, decision.Action) is { IsSensitive: true })
                {
                    continue;
                }

                if (!byEntity.TryGetValue(entity.Name, out List<AccessDecision>? granted))
                {
                    granted = [];
                    byEntity[entity.Name] = granted;
                }

                granted.Add(decision);
            }

            // --- The entries, in the catalogue's order: standard operations first, then the entity's own actions ---
            List<WhoamiEntityAccess> matrix = [];
            IEnumerable<string> order = entities.Stacks.Select(static stack => stack.Resource).Concat(entities.Children.Select(static child => child.Resource));
            foreach (string entity in order)
            {
                if (!byEntity.TryGetValue(entity, out List<AccessDecision>? granted))
                {
                    continue;
                }

                List<string> operations = [.. StandardOperations.Where(operation => granted.Any(decision => string.Equals(decision.Action, operation, StringComparison.Ordinal)))];
                List<string> actions = [.. granted.Select(static decision => decision.Action).Where(action => !StandardOperations.Contains(action, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];
                List<string> filtered = [.. granted.Where(static decision => decision.Outcome == AccessOutcome.Filtered).Select(static decision => decision.Action).Distinct(StringComparer.Ordinal)];
                matrix.Add(new WhoamiEntityAccess(entity, operations, actions, filtered));
            }

            return matrix;
        }

        /// <summary>Builds the output schema: the result's shape as the wire's JSON writes it.</summary>
        /// <returns>The schema.</returns>
        private static JsonElement BuildOutputSchema()
        {
            static JsonObject Text()
            {
                return new() { ["type"] = "string" };
            }

            static JsonObject Texts()
            {
                return new() { ["type"] = "array", ["items"] = Text() };
            }

            JsonObject user = ToolSchema.Closed(
                [
                    ("id", new JsonObject { ["type"] = "integer" }),
                    ("name", Text()),
                    ("email", Text()),
                    ("language", Text()),
                    ("calendar", Text()),
                    ("timeZone", Text()),
                ],
                ["id", "name", "language", "calendar", "timeZone"]);
            JsonObject tenant = ToolSchema.Closed(
                [
                    ("id", new JsonObject { ["type"] = "integer" }),
                    ("name", Text()),
                    ("category", new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(nameof(TenantCategory.Live), nameof(TenantCategory.Sandbox)) }),
                    ("languages", Texts()),
                    ("calendars", Texts()),
                    ("timeZone", Text()),
                ],
                ["id", "name", "category", "languages", "calendars", "timeZone"]);
            JsonObject access = ToolSchema.Closed(
                [
                    ("entity", Text()),
                    ("operations", Texts()),
                    ("actions", Texts()),
                    ("filtered", Texts()),
                ],
                ["entity", "operations", "actions", "filtered"]);
            JsonObject schema = ToolSchema.Closed(
                [
                    ("user", user),
                    ("tenant", tenant),
                    ("access", new JsonObject { ["type"] = "array", ["items"] = access }),
                    ("tags", new JsonObject { ["type"] = "object", ["additionalProperties"] = Text() }),
                ],
                ["user", "tenant", "access", "tags"]);
            schema.Insert(0, "$schema", ToolSchema.Dialect);
            return JsonSerializer.SerializeToElement(schema);
        }
    }
}
