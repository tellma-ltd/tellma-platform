// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The agent tool endpoint's rate and timeout: the two values of the MCP server's options the
    ///     web host's limiter and timeout policies read. Never bound from configuration itself: the
    ///     MCP server, when a distribution composes it, binds and validates its own options and sets
    ///     these from them, and a host without the MCP server keeps the defaults, so the policies
    ///     exist either way.
    /// </summary>
    internal sealed class McpLimitOptions
    {
        /// <summary>The default number of tool calls one user makes in one tenant per minute, on each instance.</summary>
        public const int DefaultToolCallsPerMinutePerUser = 120;

        /// <summary>The default time one tool call may run.</summary>
        public static readonly TimeSpan DefaultToolTimeout = TimeSpan.FromSeconds(60);

        /// <summary>The longest time one tool call may be allowed to run: the web API's own bound.</summary>
        public static TimeSpan MaxToolTimeout => ApiOptions.MaxTimeout;

        /// <summary>The tool calls one user makes in one tenant per minute, on each instance; 120 by default.</summary>
        public int ToolCallsPerMinutePerUser { get; set; } = DefaultToolCallsPerMinutePerUser;

        /// <summary>How long one tool call may run; 60 seconds by default.</summary>
        public TimeSpan ToolTimeout { get; set; } = DefaultToolTimeout;
    }
}
