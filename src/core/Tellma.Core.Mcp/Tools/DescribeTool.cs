// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Globalization;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Mcp.Describe;

namespace Tellma.Core.Mcp.Tools
{
    /// <summary>
    ///     <c>tellma_describe</c>: without an entity, the catalogue of the entities the tools reach,
    ///     narrowed by search words; with one, its columns, navigations, collections, operations,
    ///     actions and example filters. A description names; it grants nothing.
    /// </summary>
    internal sealed class DescribeTool : McpTool
    {
        /// <summary>The tool's description, written for the model.</summary>
        public const string ToolDescription =
            "Without entity: lists the entities of this tenant, one line each with its title, narrowed to those whose name, title or description contains every word of search. "
            + "With entity: describes it in Markdown: its properties with their types, which are editable, its navigations and the columns reachable through them, "
            + "its child collections, operations, actions with their argument schemas, and example filters. "
            + "Call it before " + McpToolNames.Query + ", " + McpToolNames.Aggregate + " or " + McpToolNames.Get + " to learn an entity's columns; never guess column names. "
            + "Being described grants nothing: permissions are checked on every call.";

        /// <summary>The <c>entity</c> argument's name.</summary>
        private const string EntityArgument = "entity";

        /// <summary>The <c>search</c> argument's name.</summary>
        private const string SearchArgument = "search";

        /// <summary>Defines the tool.</summary>
        public DescribeTool()
            : base(McpToolNames.Describe, "Describe entities", ToolDescription, Schema(), idempotent: true)
        {
        }

        /// <inheritdoc />
        protected override Task<CallToolResult> InvokeAsync(ToolArguments arguments, IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            ArgumentNullException.ThrowIfNull(services);
            EntityDescriber describer = services.GetRequiredService<EntityDescriber>();
            ILabelProvider labels = services.GetRequiredService<ILabelProvider>();
            string? search = arguments.String(SearchArgument);

            // --- The catalogue, cheap enough to render on every call ---
            if (arguments.String(EntityArgument) is not string name)
            {
                return Task.FromResult(Text(services, EntityDescriber.RenderCatalogue(describer.Catalogue(labels), search), truncated: false));
            }

            if (search is not null)
            {
                throw ToolArguments.Invalid(SearchArgument, "search narrows the catalogue; give either entity or search.");
            }

            // --- One entity, rendered once per tenant, language and settings tag ---
            McpEntity entity = services.GetRequiredService<McpEntities>().Require(name, EntityArgument);
            RequestContext context = services.GetRequiredService<IRequestContextAccessor>().Current;
            TenantSettings settings = context.TenantSettings
                ?? throw new InvalidOperationException("The request's context carries no tenant settings after its front door admitted it.");
            string text = services.GetRequiredService<DescribeCache>().GetOrRender(
                context.RequireTenant().Id,
                entity.Name,
                CultureInfo.CurrentUICulture.Name,
                settings.Tag,
                () => describer.Describe(entity, settings.Shape, labels));
            return Task.FromResult(Text(services, text, truncated: false));
        }

        /// <summary>The tool's input schema.</summary>
        /// <returns>The schema.</returns>
        private static ToolInput Schema()
        {
            return ToolSchema.Input(
                [
                    (EntityArgument, ToolSchema.String("The entity to describe, such as gl.Center, matched ignoring case; the catalogue when absent.")),
                    (SearchArgument, ToolSchema.String("Words that narrow the catalogue, matched ignoring case; with no entity only.")),
                ],
                []);
        }
    }
}
