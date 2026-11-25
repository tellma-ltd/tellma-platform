// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>
    ///     Stands in for an exporter on the host's OpenTelemetry pipeline: it keeps a copy of every log
    ///     record and every span the pipeline would export, since the pipeline reuses a record once its
    ///     processors return.
    /// </summary>
    internal sealed class TelemetryRecorder
    {
        /// <summary>The log records exported so far.</summary>
        private readonly ConcurrentQueue<ExportedLog> _logs = new();

        /// <summary>The spans exported so far.</summary>
        private readonly ConcurrentQueue<ExportedSpan> _spans = new();

        /// <summary>The log records exported so far, in order.</summary>
        public IReadOnlyList<ExportedLog> Logs => [.. _logs];

        /// <summary>The spans exported so far, in order.</summary>
        public IReadOnlyList<ExportedSpan> Spans => [.. _spans];

        /// <summary>Adds the recorder's processors to a host's log and trace pipelines.</summary>
        /// <param name="services">The host's services.</param>
        public void AddTo(IServiceCollection services)
        {
            _ = services.ConfigureOpenTelemetryLoggerProvider(builder => builder.AddProcessor(new LogProcessor(_logs)));
            _ = services.ConfigureOpenTelemetryTracerProvider(builder => builder.AddProcessor(new SpanProcessor(_spans)));
        }

        /// <summary>Copies each log record as it ends.</summary>
        /// <param name="logs">Where the copies go.</param>
        private sealed class LogProcessor(ConcurrentQueue<ExportedLog> logs) : BaseProcessor<LogRecord>
        {
            /// <inheritdoc />
            public override void OnEnd(LogRecord data)
            {
                ArgumentNullException.ThrowIfNull(data);

                // The last value of an attribute named twice wins, as an exporter writes it.
                Dictionary<string, object?> attributes = new(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> attribute in data.Attributes ?? [])
                {
                    attributes[attribute.Key] = attribute.Value;
                }

                logs.Enqueue(new ExportedLog(data.CategoryName, data.LogLevel, data.EventId.Name, data.FormattedMessage, data.TraceId, attributes));
            }
        }

        /// <summary>Copies each span as it ends.</summary>
        /// <param name="spans">Where the copies go.</param>
        private sealed class SpanProcessor(ConcurrentQueue<ExportedSpan> spans) : BaseProcessor<Activity>
        {
            /// <inheritdoc />
            public override void OnEnd(Activity data)
            {
                ArgumentNullException.ThrowIfNull(data);
                spans.Enqueue(new ExportedSpan(data.OperationName, data.DisplayName, data.TraceId));
            }
        }
    }

    /// <summary>A copy of one exported log record.</summary>
    /// <param name="Category">Its logger category.</param>
    /// <param name="Level">Its level.</param>
    /// <param name="EventName">Its event's name; null when it has none.</param>
    /// <param name="Message">Its rendered message; null when none was rendered.</param>
    /// <param name="TraceId">The trace it was written under; the default when none.</param>
    /// <param name="Attributes">Its attributes, by name.</param>
    internal sealed record ExportedLog(
        string? Category, LogLevel Level, string? EventName, string? Message, ActivityTraceId TraceId, IReadOnlyDictionary<string, object?> Attributes)
    {
        /// <summary>An attribute's value as invariant text.</summary>
        /// <param name="name">The attribute's name.</param>
        /// <returns>The text; null when the record has no such attribute.</returns>
        public string? Text(string name)
        {
            return Attributes.TryGetValue(name, out object? value) ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim('"') : null;
        }
    }

    /// <summary>A copy of one exported span.</summary>
    /// <param name="OperationName">Its operation's name.</param>
    /// <param name="DisplayName">Its display name.</param>
    /// <param name="TraceId">Its trace.</param>
    internal sealed record ExportedSpan(string OperationName, string DisplayName, ActivityTraceId TraceId);
}
