// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using Tellma.Core.Mcp.Observability;

namespace Tellma.Core.Mcp.Tools
{
    /// <summary>
    ///     One of the platform's tools: a fixed protocol definition (a closed input schema, the
    ///     closed-domain hint, no output schema unless the tool says otherwise) and an invocation that
    ///     reads its arguments against that schema and resolves everything else from the request.
    /// </summary>
    /// <remarks>
    ///     A tool instance is shared by every request; it holds no state of its own. The request
    ///     filter has already admitted the caller when the tool runs, and maps every exception the
    ///     tool lets escape to an error result.
    /// </remarks>
    internal abstract class McpTool : McpServerTool
    {
        /// <summary>The argument names the tool's schema declares.</summary>
        private readonly IReadOnlyList<string> _arguments;

        /// <summary>Defines the tool.</summary>
        /// <param name="name">The tool's name.</param>
        /// <param name="title">The tool's title, which a client shows.</param>
        /// <param name="description">What the tool does, written for the model.</param>
        /// <param name="input">The tool's input schema.</param>
        /// <param name="idempotent">Whether the tool declares that calling it again with the same arguments answers the same; false leaves the hint unstated.</param>
        /// <param name="outputSchema">The schema of its structured content; null for a tool that answers text alone.</param>
        protected McpTool(string name, string title, string description, ToolInput input, bool idempotent, JsonElement? outputSchema = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(input);
            _arguments = input.Names;

            // Every platform tool reads the tenant's own database, a closed domain; this release's
            // tools are reads, so none declares itself destructive.
            ProtocolTool = new Tool
            {
                Name = name,
                Title = title,
                Description = description,
                InputSchema = input.Schema,
                OutputSchema = outputSchema,
                Annotations = new ToolAnnotations
                {
                    Title = title,
                    ReadOnlyHint = true,
                    IdempotentHint = idempotent ? true : null,
                    OpenWorldHint = false,
                },
            };
        }

        /// <inheritdoc />
        public sealed override Tool ProtocolTool { get; }

        /// <inheritdoc />
        public sealed override IReadOnlyList<object> Metadata { get; } = [];

        /// <inheritdoc />
        public sealed override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            IServiceProvider services = request.Services
                ?? throw new InvalidOperationException("A tool call carries no request services; the server is not composed as the platform composes it.");
            ToolArguments arguments = new(request.Params?.Arguments, _arguments);
            return await InvokeAsync(arguments, services, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs the tool.</summary>
        /// <param name="arguments">The call's arguments, read against the tool's schema.</param>
        /// <param name="services">The request's services.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The result.</returns>
        protected abstract Task<CallToolResult> InvokeAsync(ToolArguments arguments, IServiceProvider services, CancellationToken cancellationToken);

        /// <summary>A result of one text block, recording whether the text was truncated.</summary>
        /// <param name="services">The request's services.</param>
        /// <param name="text">The text.</param>
        /// <param name="truncated">Whether rows or entities were left out to stay under the result cap.</param>
        /// <returns>The result.</returns>
        protected static CallToolResult Text(IServiceProvider services, string text, bool truncated)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (truncated)
            {
                services.GetRequiredService<McpCallState>().Outcome = McpToolOutcome.Truncated;
            }

            return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
        }
    }

    /// <summary>What a tool reports of its call beyond its result: how the call ended, when that is not plain success. Scoped.</summary>
    internal sealed class McpCallState
    {
        /// <summary>The outcome a successful result stands for: <see cref="McpToolOutcome.Ok" /> unless the tool says otherwise.</summary>
        public McpToolOutcome Outcome { get; set; } = McpToolOutcome.Ok;
    }
}
