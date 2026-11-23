// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Mcp.Describe;
using Tellma.Core.Mcp.Hosting;
using Tellma.Core.Mcp.Resources;
using Tellma.Core.Mcp.Tools;

namespace Tellma.Core.Mcp.Tests.Hosting
{
    /// <summary>
    ///     The server's static shape: the five read tools with closed schemas and their annotations,
    ///     the list's fixed order, the reserved prefix, the options' validation, the origin rule, the
    ///     catalogue's search, and the instructions naming the tenant's category.
    /// </summary>
    public sealed class ServerShapeTests
    {
        /// <summary>The platform's read tools, built under the default options.</summary>
        private static readonly McpTool[] Tools =
        [
            new WhoamiTool(),
            new DescribeTool(),
            new QueryTool(Options.Create(new McpOptions())),
            new AggregateTool(Options.Create(new McpOptions())),
            new GetTool(Options.Create(new McpOptions())),
        ];

        [Fact]
        public void Every_tool_is_read_only_closed_world_and_takes_a_closed_2020_12_schema()
        {
            foreach (Tool tool in Tools.Select(static tool => tool.ProtocolTool))
            {
                Assert.True(tool.Annotations is { ReadOnlyHint: true, OpenWorldHint: false }, tool.Name);
                Assert.Equal("https://json-schema.org/draft/2020-12/schema", tool.InputSchema.GetProperty("$schema").GetString());
                Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean(), tool.Name);
            }

            // Whoami alone declares an output schema; the idempotent ones are those whose answer a repeat cannot change.
            Assert.Equal(["tellma_whoami"], Tools.Where(static tool => tool.ProtocolTool.OutputSchema is not null).Select(static tool => tool.ProtocolTool.Name));
            Assert.Equal(
                ["tellma_whoami", "tellma_describe", "tellma_aggregate"],
                Tools.Where(static tool => tool.ProtocolTool.Annotations!.IdempotentHint == true).Select(static tool => tool.ProtocolTool.Name));
        }

        [Fact]
        public void The_list_puts_the_platform_s_tools_first_in_their_order_then_others_by_name()
        {
            Tool[] shuffled = [new() { Name = "zeta" }, new() { Name = McpToolNames.Get }, new() { Name = "alpha" }, new() { Name = McpToolNames.Whoami }];

            Assert.Equal([McpToolNames.Whoami, McpToolNames.Get, "alpha", "zeta"], McpToolCallFilter.Order(shuffled).Select(static tool => tool.Name));
        }

        [Fact]
        public void A_distribution_tool_may_not_take_the_platform_s_prefix_or_a_taken_name()
        {
            var foreign = McpServerTool.Create(static () => "x", new McpServerToolCreateOptions { Name = "tellma_credit" });
            var credit = McpServerTool.Create(static () => "y", new McpServerToolCreateOptions { Name = "credit" });
            var twin = McpServerTool.Create(static () => "z", new McpServerToolCreateOptions { Name = "credit" });

            IReadOnlyList<CompositionProblem> problems = McpToolNameCheck.Check([.. Tools, foreign, credit, twin]);

            Assert.Equal(2, problems.Count);
            Assert.Contains("'tellma_credit'", problems[0].Problem, StringComparison.Ordinal);
            Assert.Equal("Two MCP tools are named 'credit'.", problems[1].Problem);
            Assert.Empty(McpToolNameCheck.Check(Tools));
        }

        [Fact]
        public void The_options_refuse_a_default_past_its_ceiling_an_unknown_language_and_an_origin_with_a_path()
        {
            McpOptions options = new()
            {
                DefaultTop = 600,
                MessageLanguage = "xx-nowhere",
                AllowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https://claude.ai/chat" },
            };

            List<ValidationResult> results = [.. options.Validate(new ValidationContext(options))];

            Assert.Equal(
                [nameof(McpOptions.DefaultTop), nameof(McpOptions.MessageLanguage), nameof(McpOptions.AllowedOrigins)],
                results.Select(static result => result.MemberNames.Single()));
            Assert.Empty(new McpOptions().Validate(new ValidationContext(new McpOptions())));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("https://claude.ai", true)]
        [InlineData("HTTPS://CHATGPT.COM", true)]
        [InlineData("https://evil.example", false)]
        [InlineData("null", false)]
        public void An_origin_passes_when_absent_or_listed(string? origin, bool allowed)
        {
            DefaultHttpContext http = new();
            if (origin is not null)
            {
                http.Request.Headers.Origin = origin;
            }

            Assert.Equal(allowed, McpOrigins.IsAllowed(http.Request, new McpOptions().AllowedOrigins));
        }

        [Fact]
        public void The_catalogue_s_search_needs_every_word_in_a_name_a_title_or_a_description()
        {
            CatalogueEntry[] entries =
            [
                new("gl.Center", "Centers", "Responsibility centers", null),
                new("acme.SalesInvoice", "Sales invoices", null, null),
                new("acme.SalesInvoiceLine", "Sales invoice lines", null, "acme.SalesInvoice"),
            ];

            string matched = EntityDescriber.RenderCatalogue(entries, "SALES lines");

            Assert.Contains("- acme.SalesInvoiceLine — Sales invoice lines (child entity of acme.SalesInvoice", matched, StringComparison.Ordinal);
            Assert.DoesNotContain("gl.Center", matched, StringComparison.Ordinal);
            Assert.DoesNotContain("- acme.SalesInvoice —", matched, StringComparison.Ordinal);
            Assert.Contains("- gl.Center — Centers: Responsibility centers", EntityDescriber.RenderCatalogue(entries, null), StringComparison.Ordinal);
            Assert.Contains("No entity matches.", EntityDescriber.RenderCatalogue(entries, "payroll"), StringComparison.Ordinal);
        }

        [Fact]
        public void The_instructions_name_a_sandbox_as_a_copy_and_an_unadmitted_caller_learns_no_tenant()
        {
            string sandbox = McpServerIdentity.Instructions("Acme ERP", ("Acme Sandbox", TenantCategory.Sandbox));
            string unadmitted = McpServerIdentity.Instructions("Acme ERP", null);

            Assert.Contains("\"Acme Sandbox\", a sandbox", sandbox, StringComparison.Ordinal);
            Assert.Contains(McpServerIdentity.Rule, sandbox, StringComparison.Ordinal);
            Assert.Contains(QueryexSyntaxResource.ResourceUri, sandbox, StringComparison.Ordinal);
            Assert.DoesNotContain("Sandbox", unadmitted, StringComparison.Ordinal);
        }

        [Fact]
        public void The_whoami_output_schema_is_closed_and_requires_its_four_members()
        {
            JsonElement schema = WhoamiTool.OutputSchema;

            Assert.Equal(["user", "tenant", "access", "tags"], schema.GetProperty("required").EnumerateArray().Select(static name => name.GetString()));
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        }

        [Fact]
        public void The_syntax_resource_is_the_embedded_reference()
        {
            QueryexSyntaxResource resource = new();

            Assert.True(resource.IsMatch("tellma://queryex/syntax"));
            Assert.False(resource.IsMatch("tellma://queryex/other"));
            Assert.StartsWith("# Queryex syntax", QueryexSyntaxResource.Text, StringComparison.Ordinal);
            Assert.Equal("text/markdown", resource.ProtocolResource.MimeType);
        }
    }
}
