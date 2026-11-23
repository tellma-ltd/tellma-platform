// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Mcp.Observability
{
    /// <summary>Source-generated log messages of the tenant MCP server.</summary>
    /// <remarks>
    ///     Messages name tools and outcomes alone: never an argument, a row or a result's text, which
    ///     carry tenant content.
    /// </remarks>
    internal static partial class McpLog
    {
        /// <summary>The category the server logs under.</summary>
        public const string Category = "Tellma.Core.Mcp";

        /// <summary>A tool call finished.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tool">The tool's name.</param>
        /// <param name="outcome">How it ended: <c>ok</c>, <c>error</c>, <c>denied</c>, <c>truncated</c> or <c>confirm</c>.</param>
        /// <param name="chars">The characters of its result.</param>
        [LoggerMessage(
            EventName = "McpToolCalled",
            Level = LogLevel.Information,
            Message = "The tool {Tool} answered {Outcome} in {Chars} characters.")]
        public static partial void ToolCalled(ILogger logger, string tool, string outcome, long chars);

        /// <summary>A tool call failed with an exception outside the mapped set, answered as the internal problem.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="tool">The tool's name.</param>
        [LoggerMessage(
            EventName = "McpToolFailed",
            Level = LogLevel.Error,
            Message = "The tool {Tool} failed with an unexpected exception and answered the internal problem.")]
        public static partial void ToolFailed(ILogger logger, Exception exception, string tool);

        /// <summary>A request to the MCP endpoint was refused for its browser origin.</summary>
        /// <param name="logger">The logger.</param>
        [LoggerMessage(
            EventName = "McpOriginRefused",
            Level = LogLevel.Information,
            Message = "A request to the MCP endpoint was refused: its Origin is not among the allowed origins.")]
        public static partial void OriginRefused(ILogger logger);
    }
}
