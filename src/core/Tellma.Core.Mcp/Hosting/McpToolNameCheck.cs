// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Mcp.Tools;

namespace Tellma.Core.Mcp.Hosting
{
    /// <summary>
    ///     The startup check of the server's tool names: the <c>tellma_</c> prefix belongs to the
    ///     platform, so a distribution's tool may not take it, and no two tools share a name.
    /// </summary>
    internal sealed class McpToolNameCheck : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "core.mcp-tool-names";

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            return Task.FromResult(Check(services.GetServices<McpServerTool>()));
        }

        /// <summary>Checks a server's tools.</summary>
        /// <param name="tools">The tools.</param>
        /// <returns>The problems; empty when every name is the tool's own.</returns>
        internal static IReadOnlyList<CompositionProblem> Check(IEnumerable<McpServerTool> tools)
        {
            ArgumentNullException.ThrowIfNull(tools);
            List<CompositionProblem> problems = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (McpServerTool tool in tools)
            {
                string name = tool.ProtocolTool.Name;

                // --- The platform's prefix, on a tool the platform did not write ---
                if (tool is not McpTool && name.StartsWith(McpToolNames.Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(new CompositionProblem(
                        CheckName,
                        $"The MCP tool '{name}' of {tool.GetType().FullName} takes the platform's prefix '{McpToolNames.Prefix}'.",
                        $"Rename the tool: a distribution's tool names start with anything but '{McpToolNames.Prefix}', which the platform reserves for its own tools."));
                }

                // --- One tool per name ---
                if (!seen.Add(name))
                {
                    problems.Add(new CompositionProblem(
                        CheckName,
                        $"Two MCP tools are named '{name}'.",
                        "Give each tool its own name; a client calls a tool by its name alone."));
                }
            }

            return problems;
        }
    }
}
