// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Mcp.Observability
{
    /// <summary>How a tool call ended, as the call counter's <c>outcome</c> tag names it.</summary>
    internal enum McpToolOutcome
    {
        /// <summary>The tool answered.</summary>
        Ok,

        /// <summary>The tool answered an error: invalid arguments, a validation failure, a conflict, a server failure.</summary>
        Error,

        /// <summary>The caller may not do what the call asked, or the tenant or the record is not theirs to see.</summary>
        Denied,

        /// <summary>The tool answered, with fewer rows or entities than it read, to stay under the result cap.</summary>
        Truncated,

        /// <summary>The tool answered a preview that a confirmed call completes.</summary>
        Confirm,
    }

    /// <summary>
    ///     The tenant MCP server's instruments: tool calls by tool and outcome, their durations and
    ///     their result sizes. Tag values are closed sets: the server's own tool names, never a
    ///     tenant, a user or an argument.
    /// </summary>
    internal sealed class McpMetrics
    {
        /// <summary>The tool calls, by tool and outcome.</summary>
        private readonly Counter<long> _calls;

        /// <summary>The tool calls' durations, by tool.</summary>
        private readonly Histogram<double> _duration;

        /// <summary>The tool results' sizes in characters, by tool.</summary>
        private readonly Histogram<long> _chars;

        /// <summary>Creates the instruments on the host's meter factory.</summary>
        /// <param name="meterFactory">The host's meter factory.</param>
        public McpMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(McpTelemetryNames.MeterName);
            _calls = meter.CreateCounter<long>(
                McpTelemetryNames.ToolCalls,
                description: "Tool calls of the tenant MCP server, by tool and outcome.");
            _duration = meter.CreateHistogram<double>(
                McpTelemetryNames.ToolDuration,
                unit: "s",
                description: "How long a tool call took, by tool.");
            _chars = meter.CreateHistogram<long>(
                McpTelemetryNames.ResultChars,
                unit: "{char}",
                description: "The characters of a tool call's result, by tool.");
        }

        /// <summary>The <c>outcome</c> tag's value of an outcome.</summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>The tag value.</returns>
        public static string Tag(McpToolOutcome outcome)
        {
            return outcome switch
            {
                McpToolOutcome.Ok => "ok",
                McpToolOutcome.Error => "error",
                McpToolOutcome.Denied => "denied",
                McpToolOutcome.Truncated => "truncated",
                McpToolOutcome.Confirm => "confirm",
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The outcome is not one the call counter knows."),
            };
        }

        /// <summary>Records one finished tool call.</summary>
        /// <param name="tool">The tool's name.</param>
        /// <param name="outcome">How it ended.</param>
        /// <param name="elapsed">How long it took.</param>
        /// <param name="chars">The characters of its result's text blocks.</param>
        public void RecordCall(string tool, McpToolOutcome outcome, TimeSpan elapsed, long chars)
        {
            KeyValuePair<string, object?> toolTag = new(McpTelemetryNames.ToolTag, tool);
            _calls.Add(1, toolTag, new KeyValuePair<string, object?>(McpTelemetryNames.OutcomeTag, Tag(outcome)));
            _duration.Record(elapsed.TotalSeconds, toolTag);
            _chars.Record(chars, toolTag);
        }
    }
}
