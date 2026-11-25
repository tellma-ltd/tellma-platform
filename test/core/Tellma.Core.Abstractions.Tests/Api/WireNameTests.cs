// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     The header, meter, instrument and tag names are a contract shared with the client, the
    ///     alert queries and the dashboards, so each is pinned to its exact text.
    /// </summary>
    public class WireNameTests
    {
        [Fact]
        public void The_header_names_are_pinned()
        {
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [nameof(TellmaHeaders.TimeZone)] = "Tellma-Time-Zone",
                    [nameof(TellmaHeaders.Calendar)] = "Tellma-Calendar",
                    [nameof(TellmaHeaders.Client)] = "Tellma-Client",
                    [nameof(TellmaHeaders.Activity)] = "Tellma-Activity",
                    [nameof(TellmaHeaders.VersionTags)] = "Tellma-Version-Tags",
                    [nameof(TellmaHeaders.Build)] = "Tellma-Build",
                    [nameof(TellmaHeaders.TraceId)] = "Tellma-Trace-Id",
                    [nameof(TellmaHeaders.Contract)] = "Tellma-Contract",
                    [nameof(TellmaHeaders.Confirm)] = "Tellma-Confirm",
                    [nameof(TellmaHeaders.Priority)] = "Tellma-Priority",
                },
                Constants(typeof(TellmaHeaders)));
        }

        [Fact]
        public void The_web_host_telemetry_names_are_pinned()
        {
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [nameof(ApiTelemetryNames.MeterName)] = "Tellma.Core.AspNetCore",
                    [nameof(ApiTelemetryNames.RequestsRejected)] = "tellma.api.requests.rejected",
                    [nameof(ApiTelemetryNames.Problems)] = "tellma.api.problems",
                    [nameof(ApiTelemetryNames.OperationDuration)] = "tellma.api.operation.duration",
                    [nameof(ApiTelemetryNames.UnknownMembers)] = "tellma.api.unknown_members",
                    [nameof(ApiTelemetryNames.RealtimeEvents)] = "tellma.realtime.events",
                    [nameof(ApiTelemetryNames.ResourceTag)] = "tellma.resource",
                    [nameof(ApiTelemetryNames.OperationTag)] = "tellma.operation",
                    [nameof(ApiTelemetryNames.ClientTag)] = "tellma.client",
                    [nameof(ApiTelemetryNames.ReasonTag)] = "reason",
                    [nameof(ApiTelemetryNames.LaneTag)] = "lane",
                    [nameof(ApiTelemetryNames.TypeTag)] = "type",
                    [nameof(ApiTelemetryNames.EventTag)] = "event",
                },
                Constants(typeof(ApiTelemetryNames)));
        }

        [Fact]
        public void The_rate_policy_names_are_pinned()
        {
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [nameof(TellmaRatePolicies.User)] = "tellma-user",
                    [nameof(TellmaRatePolicies.Tenant)] = "tellma-tenant",
                    [nameof(TellmaRatePolicies.Anonymous)] = "tellma-anonymous",
                    [nameof(TellmaRatePolicies.Export)] = "tellma-export",
                    [nameof(TellmaRatePolicies.Import)] = "tellma-import",
                    [nameof(TellmaRatePolicies.Reports)] = "tellma-reports",
                    [nameof(TellmaRatePolicies.Telemetry)] = "tellma-telemetry",
                    [nameof(TellmaRatePolicies.Blobs)] = "tellma-blobs",
                    [nameof(TellmaRatePolicies.Mcp)] = "tellma-mcp",
                },
                Constants(typeof(TellmaRatePolicies)));
        }

        [Fact]
        public void The_mcp_telemetry_names_are_pinned()
        {
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [nameof(McpTelemetryNames.MeterName)] = "Tellma.Core.Mcp",
                    [nameof(McpTelemetryNames.ToolCalls)] = "tellma.mcp.tool.calls",
                    [nameof(McpTelemetryNames.ToolDuration)] = "tellma.mcp.tool.duration",
                    [nameof(McpTelemetryNames.ResultChars)] = "tellma.mcp.result.chars",
                    [nameof(McpTelemetryNames.ToolTag)] = "tool",
                    [nameof(McpTelemetryNames.OutcomeTag)] = "outcome",
                },
                Constants(typeof(McpTelemetryNames)));
        }

        [Fact]
        public void The_client_telemetry_names_are_pinned()
        {
            Assert.Equal(
                new Dictionary<string, string>
                {
                    [nameof(ClientTelemetryNames.MeterName)] = "Tellma.Core.AspNetCore",
                    [nameof(ClientTelemetryNames.LoggerCategory)] = "Tellma.ClientTelemetry",
                    [nameof(ClientTelemetryNames.Errors)] = "tellma.client.errors",
                    [nameof(ClientTelemetryNames.SlowRequests)] = "tellma.client.requests.slow",
                    [nameof(ClientTelemetryNames.SourceTag)] = "source",
                    [nameof(ClientTelemetryNames.KindTag)] = "kind",
                    [nameof(ClientTelemetryNames.OutcomeTag)] = "outcome",
                },
                Constants(typeof(ClientTelemetryNames)));
        }

        /// <summary>Every public string constant a type declares, by name.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The constants.</returns>
        internal static Dictionary<string, string> Constants(Type type)
        {
            return type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
                .ToDictionary(static field => field.Name, static field => (string)field.GetRawConstantValue()!);
        }
    }
}
