// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.Mcp.Hosting;
using Tellma.Core.Mcp.Observability;

namespace Tellma.Core.Mcp.Tests.Hosting
{
    /// <summary>
    ///     The call filter's own rules: a refusal counted as denied and any other failure as an error,
    ///     the request's cancellation alone let through unanswered, and a problem's text carrying every
    ///     member a model needs to correct itself.
    /// </summary>
    public sealed class ToolCallFilterTests
    {
        [Theory]
        [InlineData(403, "forbidden", true)]
        [InlineData(404, "not-found", true)]
        [InlineData(400, McpToolCallFilter.SensitiveOperationCode, true)]
        [InlineData(400, "bad-request", false)]
        [InlineData(422, "validation", false)]
        [InlineData(500, "internal", false)]
        public void A_refusal_is_denied_and_any_other_failure_an_error(int status, string code, bool denied)
        {
            Assert.Equal(denied ? McpToolOutcome.Denied : McpToolOutcome.Error, McpToolCallFilter.Classify(new ProblemDescription(status, code)));
        }

        [Fact]
        public void The_request_s_own_cancellation_alone_goes_unanswered()
        {
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();

            Assert.False(McpToolCallFilter.IsAnswered(new OperationCanceledException(cancelled.Token), cancelled.Token));
            Assert.True(McpToolCallFilter.IsAnswered(new OperationCanceledException(), CancellationToken.None));
            Assert.True(McpToolCallFilter.IsAnswered(new TimeoutException(), cancelled.Token));
        }

        [Fact]
        public void A_problem_s_text_carries_its_code_errors_details_arguments_and_wait()
        {
            ProblemDocument document = new(
                "about:blank",
                "The request is invalid.",
                422,
                "Two lines are wrong.",
                "/1/mcp",
                "validation",
                [new ProblemErrorDocument("lines[0].amount", null, null, null, null, "Amount", "required", "Amount is required.", new Dictionary<string, object?>())],
                new { conflicts = 1 },
                new Dictionary<string, object?> { ["limit"] = "rows" });

            using var text = JsonDocument.Parse(McpToolCallFilter.Render(document, TimeSpan.FromSeconds(2.2)));
            JsonElement root = text.RootElement;

            Assert.Equal(
                ["status", "code", "title", "detail", "errors", "errorDetails", "arguments", "retryAfterSeconds"],
                root.EnumerateObject().Select(static member => member.Name));
            Assert.Equal("lines[0].amount", root.GetProperty("errors")[0].GetProperty("path").GetString());
            Assert.Equal(3, root.GetProperty("retryAfterSeconds").GetInt64());

            // --- The members a problem lacks are left out ---
            using var bare = JsonDocument.Parse(McpToolCallFilter.Render(document with { Detail = null, Errors = [], ErrorDetails = null, Arguments = null }, null));
            Assert.Equal(["status", "code", "title"], bare.RootElement.EnumerateObject().Select(static member => member.Name));
        }
    }
}
