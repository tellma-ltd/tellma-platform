// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>Who sent a batch, as the server knows it.</summary>
    /// <param name="Subject">The session's subject; null when the principal carries none.</param>
    /// <param name="UserAgent">The request's <c>User-Agent</c>; null when it has none.</param>
    internal sealed record ClientTelemetryCaller(string? Subject, string? UserAgent);

    /// <summary>
    ///     Writes a checked batch into the host's logs and the relay's counters: one record an item,
    ///     each under a scope of the batch's and the item's dimensions, and one record of what was
    ///     dropped.
    /// </summary>
    internal sealed class ClientTelemetryWriter
    {
        /// <summary>The longest <c>User-Agent</c> a record keeps.</summary>
        public const int MaxUserAgentLength = 512;

        /// <summary>
        ///     The name of the activity an item with a trace id is logged under, which belongs to no
        ///     registered source and so is never exported.
        /// </summary>
        public const string ItemActivityName = "Tellma.ClientTelemetry.Item";

        /// <summary>The dimensions every item's reserved properties become.</summary>
        private static readonly FrozenDictionary<string, string> ItemDimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["route"] = "ClientRoute",
            ["pageKind"] = "ClientPageKind",
            ["sampleRate"] = "SampleRate",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>The dimensions an error's own properties become; its class is the message's.</summary>
        private static readonly FrozenDictionary<string, string> ErrorDimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "ErrorSource",
            ["kind"] = "ErrorKind",
            ["code"] = "ErrorCode",
            ["status"] = "Status",
            ["fingerprint"] = "Fingerprint",
            [ClientTelemetryRules.StackKey] = "Stack",
            [ClientTelemetryRules.MessageKey] = "Message",
            ["recentTraceIds"] = "RecentTraceIds",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        private readonly ILogger _logger;
        private readonly ClientTelemetryMetrics _metrics;
        private readonly DeploymentVersions _versions;
        private readonly DeploymentIdentity _deployment;

        /// <summary>Creates the writer.</summary>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="metrics">The relay's counters.</param>
        /// <param name="versions">The deployment's versions.</param>
        /// <param name="deployment">The deployment's identity.</param>
        public ClientTelemetryWriter(
            ILoggerFactory loggerFactory, ClientTelemetryMetrics metrics, DeploymentVersions versions, DeploymentIdentity deployment)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ClientTelemetryNames.LoggerCategory);
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _versions = versions ?? throw new ArgumentNullException(nameof(versions));
            _deployment = deployment ?? throw new ArgumentNullException(nameof(deployment));
        }

        /// <summary>Writes a batch whose envelope the rules accepted.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="caller">Who sent it.</param>
        /// <param name="receivedAt">The server's clock when the relay received it.</param>
        public void Write(ClientTelemetryBatch batch, ClientTelemetryCaller caller, DateTimeOffset receivedAt)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(caller);

            // --- The batch's dimensions, around every record it gives ---
            using IDisposable? batchScope = _logger.BeginScope(BatchDimensions(batch, caller));

            // The client's clock is corrected by its skew, the server's clock less the client's at send.
            TimeSpan skew = receivedAt - batch.SentAt;
            int droppedKind = 0, droppedName = 0, droppedTime = 0;
            foreach (ClientTelemetryItem? item in batch.Items)
            {
                switch (ClientTelemetryRules.Check(item, batch.SentAt))
                {
                    case ClientItemDrop.Kind:
                        droppedKind++;
                        break;
                    case ClientItemDrop.Name:
                        droppedName++;
                        break;
                    case ClientItemDrop.Time:
                        droppedTime++;
                        break;
                    case ClientItemDrop.None:
                    default:
                        WriteItem(item, skew);
                        break;
                }
            }

            // --- One record of the items dropped, by reason ---
            int dropped = droppedKind + droppedName + droppedTime;
            if (dropped > 0)
            {
                Dictionary<string, object> reasons = new(StringComparer.Ordinal)
                {
                    ["DroppedKind"] = droppedKind,
                    ["DroppedName"] = droppedName,
                    ["DroppedTime"] = droppedTime,
                };
                using IDisposable? dropScope = _logger.BeginScope(reasons);
                ClientTelemetryLog.ItemsDropped(_logger, dropped);
            }
        }

        /// <summary>Writes a kept item: its record under its dimensions, its count, and its trace.</summary>
        /// <param name="item">The item.</param>
        /// <param name="skew">The batch's clock skew.</param>
        private void WriteItem(ClientTelemetryItem item, TimeSpan skew)
        {
            // --- The item's properties that hold, under the dimensions they name ---
            bool error = item.Kind == ClientTelemetryRules.ErrorKind;
            Dictionary<string, object> properties = new(StringComparer.Ordinal);
            int droppedProperties = ClientTelemetryRules.KeepProperties(item.Properties, properties);
            Dictionary<string, object> dimensions = new(StringComparer.Ordinal)
            {
                // A corrected time stays within a day of the server's clock, so it never overflows.
                ["ClientAt"] = (item.At + skew).ToString("O", CultureInfo.InvariantCulture),
            };
            foreach (KeyValuePair<string, object> property in properties)
            {
                if (error && property.Key == "errorClass")
                {
                    // The message names the class.
                    continue;
                }

                string key = ItemDimensions.GetValueOrDefault(property.Key)
                    ?? (error ? ErrorDimensions.GetValueOrDefault(property.Key) : null)
                    ?? property.Key;
                dimensions[key] = property.Value;
            }

            if (droppedProperties > 0)
            {
                dimensions["DroppedProperties"] = droppedProperties;
            }

            // --- The counters, over closed tag values alone ---
            if (error)
            {
                _metrics.RecordError(properties.GetValueOrDefault("source"), properties.GetValueOrDefault("kind"));
            }
            else if (item.Name == ClientTelemetryRules.SlowRequestName)
            {
                _metrics.RecordSlowRequest(properties.GetValueOrDefault("outcome"));
            }

            // --- The record, under an unexported activity of the item's trace when it names one ---
            // A trace id that is not one is ignored, not a reason to drop the item.
            Activity? current = Activity.Current;
            Activity? trace = ClientTelemetryRules.IsTraceId(item.TraceId) ? Correlate(item.TraceId!) : null;
            try
            {
                using IDisposable? itemScope = _logger.BeginScope(dimensions);
                if (error)
                {
                    ClientTelemetryLog.ClientError(_logger, Text(properties, "errorClass"), Text(properties, "route"));
                }
                else
                {
                    ClientTelemetryLog.ClientEvent(_logger, item.Name);
                }
            }
            finally
            {
                // Disposing stops the activity; one whose parent is remote leaves no activity current, so the caller's is restored.
                trace?.Dispose();
                Activity.Current = current;
            }
        }

        /// <summary>The dimensions of the batch: the caller's, the client's and the deployment's.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="caller">Who sent it.</param>
        /// <returns>The dimensions.</returns>
        private Dictionary<string, object> BatchDimensions(ClientTelemetryBatch batch, ClientTelemetryCaller caller)
        {
            Dictionary<string, object> dimensions = new(StringComparer.Ordinal)
            {
                ["ClientVersion"] = batch.Version,
                ["ClientOutdated"] = !string.Equals(batch.Version, _versions.DistributionVersion, StringComparison.Ordinal),
                ["ClientTabId"] = batch.TabId,
                [ObservabilityComposition.DeploymentIdTag] = _deployment.DeploymentId,
            };
            if (caller.Subject is string subject)
            {
                dimensions["Subject"] = subject;
            }

            if (caller.UserAgent is { Length: > 0 } userAgent)
            {
                dimensions["UserAgent"] = userAgent.Length > MaxUserAgentLength ? userAgent[..MaxUserAgentLength] : userAgent;
            }

            if (batch.TenantId is int tenantId)
            {
                dimensions["ClientTenantId"] = tenantId;
            }

            return dimensions;
        }

        /// <summary>
        ///     Starts an activity parented to a trace id and a random span id, so a record written while
        ///     it is current carries the trace id. It is not sampled and belongs to no registered source,
        ///     so nothing exports it.
        /// </summary>
        /// <param name="traceId">The trace id, 32 lowercase hexadecimal digits.</param>
        /// <returns>The started activity.</returns>
        private static Activity Correlate(string traceId)
        {
            Activity activity = new(ItemActivityName);
            _ = activity.SetIdFormat(ActivityIdFormat.W3C);
            _ = activity.SetParentId(ActivityTraceId.CreateFromString(traceId), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None);
            return activity.Start();
        }

        /// <summary>A kept text property, or <see cref="ClientTelemetryLog.None" />.</summary>
        /// <param name="properties">The kept properties.</param>
        /// <param name="key">The property's key.</param>
        /// <returns>The text.</returns>
        private static string Text(Dictionary<string, object> properties, string key)
        {
            return properties.GetValueOrDefault(key) as string ?? ClientTelemetryLog.None;
        }
    }
}
