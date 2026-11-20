// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The agent tool endpoint's rate and timeout, bound from <c>Tellma:Mcp</c>: the two values
    ///     of the MCP server's options the web host's limiter and timeout policies read, so the
    ///     policies exist whether or not a distribution composes the MCP server.
    /// </summary>
    internal sealed class McpLimitOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Mcp";

        /// <summary>The tool calls one user makes in one tenant per minute, on each instance; 120 by default.</summary>
        public int ToolCallsPerMinutePerUser { get; set; } = 120;

        /// <summary>How long one tool call may run; 60 seconds by default.</summary>
        public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The rate: positive, or every tool call would be refused ---
            if (ToolCallsPerMinutePerUser <= 0)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(ToolCallsPerMinutePerUser)} is {ToolCallsPerMinutePerUser}; it must be positive.",
                    [nameof(ToolCallsPerMinutePerUser)]);
            }

            // --- The timeout: positive and bounded, as the web API's are ---
            if (ToolTimeout <= TimeSpan.Zero || ToolTimeout > ApiOptions.MaxTimeout)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(ToolTimeout)} is {ToolTimeout}; it must be positive and at most {ApiOptions.MaxTimeout}.",
                    [nameof(ToolTimeout)]);
            }
        }
    }
}
