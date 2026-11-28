// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The names of the tenant MCP server's meter, its instruments and their tag keys. Tag values
    ///     are closed sets, and no instrument carries a tenant or user tag.
    /// </summary>
    public static class McpTelemetryNames
    {
        /// <summary>The tenant MCP server's meter.</summary>
        public const string MeterName = "Tellma.Core.Mcp";

        /// <summary>
        ///     Counter of tool calls. Tags: <see cref="ToolTag" />, <see cref="OutcomeTag" />
        ///     (<c>ok</c>, <c>error</c>, <c>denied</c>, <c>truncated</c> or <c>confirm</c>).
        /// </summary>
        public const string ToolCalls = "tellma.mcp.tool.calls";

        /// <summary>Histogram of tool call durations, in seconds. Tag: <see cref="ToolTag" />.</summary>
        public const string ToolDuration = "tellma.mcp.tool.duration";

        /// <summary>Histogram of tool result sizes, in characters. Tag: <see cref="ToolTag" />.</summary>
        public const string ResultChars = "tellma.mcp.result.chars";

        /// <summary>Tag key: the tool's name.</summary>
        public const string ToolTag = "tool";

        /// <summary>Tag key: how the call ended.</summary>
        public const string OutcomeTag = "outcome";
    }
}
