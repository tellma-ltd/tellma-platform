// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.Mcp.Observability;
using Tellma.Core.Mcp.Tools;

namespace Tellma.Core.Mcp.Hosting
{
    /// <summary>
    ///     The request filters of the tenant MCP server. Every tool call, the platform's and a
    ///     distribution's alike, passes the tenant's front door first; it then runs in the request's
    ///     cultures, and whatever it lets escape is mapped exactly as the web surface maps it and
    ///     answered as an <c>isError</c> result the model reads to correct itself. Each call is
    ///     counted, timed and logged with its outcome.
    /// </summary>
    internal static class McpToolCallFilter
    {
        /// <summary>The problem code of a sensitive operation, which counts as denied.</summary>
        public const string SensitiveOperationCode = "sensitive-operation";

        /// <summary>The filter around a tool call.</summary>
        /// <param name="next">The call's handler: the tool.</param>
        /// <returns>The filtered handler.</returns>
        public static McpRequestHandler<CallToolRequestParams, CallToolResult> CallTool(McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, cancellationToken) =>
            {
                // An unknown tool is the protocol's error, which the SDK answers.
                if (request.MatchedPrimitive is null)
                {
                    return await next(request, cancellationToken).ConfigureAwait(false);
                }

                // A tool that matched always runs inside its request; one that does not is refused, never run unguarded.
                if (request.Services is not IServiceProvider services)
                {
                    throw new InvalidOperationException($"The MCP tool '{request.Params?.Name}' was called without its request's services, so its tenant's front door cannot run.");
                }

                string tool = request.Params?.Name ?? string.Empty;
                long started = Stopwatch.GetTimestamp();
                ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(McpLog.Category);
                McpCallState state = services.GetRequiredService<McpCallState>();
                McpRequestAccess access = services.GetRequiredService<McpRequestAccess>();

                // --- The front door, decided once per request; then the tool, in the request's cultures ---
                CallToolResult result;
                McpToolOutcome outcome;
                Exception? refused = await access.EnsureAsync(cancellationToken).ConfigureAwait(false);
                if (refused is not null)
                {
                    (result, outcome) = Problem(services, refused, tool, logger);
                }
                else
                {
                    access.ApplyCultures();
                    try
                    {
                        result = await next(request, cancellationToken).ConfigureAwait(false);
                        outcome = result.IsError == true ? McpToolOutcome.Error : state.Outcome;
                    }
                    catch (Exception exception) when (IsAnswered(exception, cancellationToken))
                    {
                        (result, outcome) = Problem(services, exception, tool, logger);
                    }
                }

                // --- Counted, timed and logged once per call ---
                long chars = result.Content.OfType<TextContentBlock>().Sum(static block => (long)block.Text.Length);
                services.GetRequiredService<McpMetrics>().RecordCall(tool, outcome, Stopwatch.GetElapsedTime(started), chars);
                string tag = McpMetrics.Tag(outcome);
                McpLog.ToolCalled(logger, tool, tag, chars);
                return result;
            };
        }

        /// <summary>The filter around the tool list: the list's cache hints, and the platform's tools first in their fixed order.</summary>
        /// <param name="next">The list's handler.</param>
        /// <returns>The filtered handler.</returns>
        public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListTools(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next)
        {
            ArgumentNullException.ThrowIfNull(next);
            return async (request, cancellationToken) =>
            {
                ListToolsResult result = await next(request, cancellationToken).ConfigureAwait(false);
                McpOptions options = request.Services!.GetRequiredService<IOptions<McpOptions>>().Value;

                // --- One list for every caller: deterministic, and cacheable by anyone for its lifetime ---
                result.Tools = [.. Order(result.Tools)];
                result.TimeToLive = TimeSpan.FromMilliseconds(options.ListTtlMs);
                result.CacheScope = CacheScope.Public;
                return result;
            };
        }

        /// <summary>Orders tools: the platform's in their declared order, then the distribution's by name.</summary>
        /// <param name="tools">The tools.</param>
        /// <returns>The tools, ordered.</returns>
        internal static IEnumerable<Tool> Order(IEnumerable<Tool> tools)
        {
            ArgumentNullException.ThrowIfNull(tools);
            return tools
                .OrderBy(static tool => Rank(tool.Name))
                .ThenBy(static tool => tool.Name, StringComparer.Ordinal);
        }

        /// <summary>A tool's place in the list: a platform tool's declared position, or after them all.</summary>
        /// <param name="name">The tool's name.</param>
        /// <returns>The rank.</returns>
        private static int Rank(string name)
        {
            for (int index = 0; index < McpToolNames.All.Count; index++)
            {
                if (string.Equals(McpToolNames.All[index], name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        /// <summary>An exception as the <c>isError</c> result a model reads: the web surface's problem, rendered as JSON text.</summary>
        /// <param name="services">The request's services.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="tool">The tool called.</param>
        /// <param name="logger">The server's logger.</param>
        /// <returns>The result, and the call's outcome.</returns>
        private static (CallToolResult Result, McpToolOutcome Outcome) Problem(IServiceProvider services, Exception exception, string tool, ILogger logger)
        {
            HttpContext http = services.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new InvalidOperationException("A tool call runs inside its HTTP request.", exception);
            ProblemDescription problem = ProblemMapper.Map(exception, ProblemMappingContext.Default);
            if (problem.IsUnhandled)
            {
                McpLog.ToolFailed(logger, exception, tool);
            }

            // --- The problem, rendered in the request's localization as the web surface renders it ---
            ProblemDocument document;
            using (ProblemWriter.Localize(http, problem.Code))
            {
                document = ProblemRenderer.Render(problem, services, TraceIdentity.Of(http));
            }

            return (new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = Render(document, problem.RetryAfter) }] }, Classify(problem));
        }

        /// <summary>Whether an exception a tool let escape is answered as an <c>isError</c> result: all but the request's own cancellation, which ends the request.</summary>
        /// <param name="exception">The exception.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>True when it is answered.</returns>
        internal static bool IsAnswered(Exception exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
        }

        /// <summary>A refused call's outcome: denied when the caller may not do it, an error otherwise.</summary>
        /// <param name="problem">The mapped problem.</param>
        /// <returns>The outcome.</returns>
        internal static McpToolOutcome Classify(ProblemDescription problem)
        {
            ArgumentNullException.ThrowIfNull(problem);
            return problem.Status is StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound
                || string.Equals(problem.Code, SensitiveOperationCode, StringComparison.Ordinal)
                ? McpToolOutcome.Denied
                : McpToolOutcome.Error;
        }

        /// <summary>A problem's text: its code, title, detail, errors and details, as compact JSON.</summary>
        /// <param name="document">The rendered problem.</param>
        /// <param name="retryAfter">The wait before a retry, if the problem names one.</param>
        /// <returns>The text.</returns>
        internal static string Render(ProblemDocument document, TimeSpan? retryAfter)
        {
            ArgumentNullException.ThrowIfNull(document);
            JsonObject text = new()
            {
                ["status"] = document.Status,
                ["code"] = document.Code,
                ["title"] = document.Title,
            };
            if (document.Detail is string detail)
            {
                text["detail"] = detail;
            }

            if (document.Errors.Count > 0)
            {
                text["errors"] = JsonSerializer.SerializeToNode(document.Errors, TellmaJson.Options);
            }

            if (document.ErrorDetails is not null)
            {
                text["errorDetails"] = JsonSerializer.SerializeToNode(document.ErrorDetails, TellmaJson.Options);
            }

            if (document.Arguments is { Count: > 0 } arguments)
            {
                text["arguments"] = JsonSerializer.SerializeToNode(arguments, TellmaJson.Options);
            }

            if (retryAfter is TimeSpan wait)
            {
                text["retryAfterSeconds"] = (long)Math.Ceiling(Math.Max(0, wait.TotalSeconds));
            }

            return text.ToJsonString(TellmaJson.Options);
        }
    }
}
