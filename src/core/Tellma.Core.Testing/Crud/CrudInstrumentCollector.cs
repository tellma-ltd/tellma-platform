// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Crud;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // The closed sets are spec 0015 §16.1's table: every tag an instrument carries, and every value
    // a tag with a closed set may take. The entity, the tenant and the user are never tags.
    /// <summary>
    ///     Collects every measurement of the pipeline's instruments on one host's meter factory, and
    ///     holds each to its instrument's closed tag set: the tags it must carry, the ones it may
    ///     carry, and the values each closed tag may take.
    /// </summary>
    /// <remarks>
    ///     The collector sees what the host's meter factory creates from the moment it is constructed,
    ///     so a suite constructs it before the operations it observes and reads it afterwards. It
    ///     collects every operation of the host, so a suite whose host serves other work at the same
    ///     time reads measurements that are not its own.
    /// </remarks>
    public sealed class CrudInstrumentCollector : IDisposable
    {
        /// <summary>The <c>crud.operation</c> values of the operation instruments.</summary>
        public static readonly FrozenSet<string> Operations = FrozenSet.Create(
            StringComparer.Ordinal,
            "query", "get", "get_by_ids", "all", "save", "delete", "delete_by_query", "delete_with_descendants", "action", "get_by_parent_ids", "excel_rows");

        /// <summary>The <c>crud.outcome</c> values of the operation instruments.</summary>
        public static readonly FrozenSet<string> Outcomes = FrozenSet.Create(
            StringComparer.Ordinal,
            "ok", "validation", "confirmation", "forbidden", "not_found", "conflict", "limit", "stale", "unavailable", "cancelled", "error");

        /// <summary>The <c>crud.source</c> values: who asked for a write.</summary>
        public static readonly FrozenSet<string> Sources = FrozenSet.Create(StringComparer.Ordinal, "web", "public_api", "agent", "import", "system");

        /// <summary>The <c>crud.lane</c> values: whether the payload's rows passed the large-batch threshold.</summary>
        public static readonly FrozenSet<string> Lanes = FrozenSet.Create(StringComparer.Ordinal, "small", "large");

        /// <summary>The <c>crud.operation</c> values of an enlisted write's host.</summary>
        public static readonly FrozenSet<string> Hosts = FrozenSet.Create(StringComparer.Ordinal, "save", "delete", "action", "api_action", "job", "provisioning");

        /// <summary>The <c>crud.operation</c> values of the validation rounds: an operation's, or an open frame's host.</summary>
        public static readonly FrozenSet<string> RoundOperations = FrozenSet.Create(StringComparer.Ordinal, [.. Operations, .. Hosts]);

        /// <summary>The <c>crud.outcome</c> values of the validation rounds.</summary>
        public static readonly FrozenSet<string> RoundOutcomes = FrozenSet.Create(StringComparer.Ordinal, "ok", "exceeded");

        /// <summary>The <c>crud.kind</c> values of the concurrency conflicts.</summary>
        public static readonly FrozenSet<string> ConflictKinds = FrozenSet.Create(StringComparer.Ordinal, "stamp", "missing", "cycle");

        /// <summary>The <c>crud.tag</c> values of the stale-context re-runs.</summary>
        public static readonly FrozenSet<string> RerunTags = FrozenSet.Create(StringComparer.Ordinal, "permissions", "settings", "entity", "guard");

        /// <summary>The <c>crud.kind</c> values of the enlisted writes.</summary>
        public static readonly FrozenSet<string> EnlistmentKinds = FrozenSet.Create(StringComparer.Ordinal, "save", "delete");

        /// <summary>The <c>crud.outcome</c> values of the enlisted writes: their host's.</summary>
        public static readonly FrozenSet<string> EnlistmentOutcomes = FrozenSet.Create(StringComparer.Ordinal, "ok", "validation", "error");

        /// <summary>The <c>crud.outcome</c> values of the confirmation warnings.</summary>
        public static readonly FrozenSet<string> ConfirmationOutcomes = FrozenSet.Create(StringComparer.Ordinal, "required", "confirmed");

        /// <summary>Every instrument's shape, by name: the tags it must carry and the ones it may, each with its closed set or null for an open one.</summary>
        private static readonly FrozenDictionary<string, Shape> Shapes = new Dictionary<string, Shape>(StringComparer.Ordinal)
        {
            [CrudTelemetryNames.Operations] = new(
                Required: Tags((CrudTelemetryNames.OperationTag, Operations), (CrudTelemetryNames.OutcomeTag, Outcomes)),
                Optional: Tags((CrudTelemetryNames.SourceTag, Sources))),
            [CrudTelemetryNames.OperationDuration] = new(
                Required: Tags((CrudTelemetryNames.OperationTag, Operations), (CrudTelemetryNames.OutcomeTag, Outcomes), (CrudTelemetryNames.LaneTag, Lanes)),
                Optional: Tags((CrudTelemetryNames.SourceTag, Sources))),
            [CrudTelemetryNames.ValidationRounds] = new(
                Required: Tags((CrudTelemetryNames.OperationTag, RoundOperations), (CrudTelemetryNames.OutcomeTag, RoundOutcomes)),
                Optional: Tags()),
            [CrudTelemetryNames.SaveEntities] = new(
                Required: Tags((CrudTelemetryNames.SourceTag, Sources), (CrudTelemetryNames.LaneTag, Lanes)),
                Optional: Tags()),
            [CrudTelemetryNames.ConcurrencyConflicts] = new(Required: Tags((CrudTelemetryNames.KindTag, ConflictKinds)), Optional: Tags()),
            [CrudTelemetryNames.ConcurrencyOverrides] = new(Required: Tags(), Optional: Tags()),
            [CrudTelemetryNames.EffectFailures] = new(Required: Tags((CrudTelemetryNames.EffectTag, null)), Optional: Tags()),
            [CrudTelemetryNames.StaleContextReruns] = new(Required: Tags((CrudTelemetryNames.TagTag, RerunTags)), Optional: Tags()),
            [CrudTelemetryNames.QueryDiscoverDuration] = new(Required: Tags(), Optional: Tags()),
            [CrudTelemetryNames.Enlistments] = new(
                Required: Tags((CrudTelemetryNames.OperationTag, Hosts), (CrudTelemetryNames.KindTag, EnlistmentKinds), (CrudTelemetryNames.OutcomeTag, EnlistmentOutcomes)),
                Optional: Tags()),
            [CrudTelemetryNames.Confirmations] = new(
                Required: Tags((CrudTelemetryNames.KeyTag, null), (CrudTelemetryNames.OutcomeTag, ConfirmationOutcomes)),
                Optional: Tags()),
        }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>The instruments whose measurements are whole numbers.</summary>
        private readonly Dictionary<string, MetricCollector<long>> _counts = new(StringComparer.Ordinal);

        /// <summary>The instruments whose measurements are durations in seconds.</summary>
        private readonly Dictionary<string, MetricCollector<double>> _durations = new(StringComparer.Ordinal);

        /// <summary>Starts collecting every pipeline instrument the host's meter factory creates.</summary>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <exception cref="ArgumentNullException"><paramref name="meterFactory" /> is null.</exception>
        public CrudInstrumentCollector(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            foreach (string name in Shapes.Keys)
            {
                // The two durations are recorded in seconds as doubles; every other instrument counts.
                if (name is CrudTelemetryNames.OperationDuration or CrudTelemetryNames.QueryDiscoverDuration)
                {
                    _durations[name] = new MetricCollector<double>(meterFactory, CrudTelemetryNames.MeterName, name);
                }
                else
                {
                    _counts[name] = new MetricCollector<long>(meterFactory, CrudTelemetryNames.MeterName, name);
                }
            }
        }

        /// <summary>Every pipeline instrument's name, in ordinal order.</summary>
        public static IReadOnlyList<string> Instruments { get; } = [.. Shapes.Keys.Order(StringComparer.Ordinal)];

        /// <summary>The measurements one instrument recorded so far, in the order recorded.</summary>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The measurements.</returns>
        /// <exception cref="ArgumentException"><paramref name="instrument" /> is not a pipeline instrument.</exception>
        public IReadOnlyList<CrudMeasurement> Snapshot(string instrument)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(instrument);
            return _counts.TryGetValue(instrument, out MetricCollector<long>? counts)
                ? [.. counts.GetMeasurementSnapshot().Select(measurement => new CrudMeasurement(instrument, measurement.Value, measurement.Tags))]
                : _durations.TryGetValue(instrument, out MetricCollector<double>? durations)
                    ? [.. durations.GetMeasurementSnapshot().Select(measurement => new CrudMeasurement(instrument, measurement.Value, measurement.Tags))]
                    : throw new ArgumentException($"'{instrument}' is not one of the pipeline's instruments.", nameof(instrument));
        }

        /// <summary>The measurements every instrument recorded so far, instrument by instrument.</summary>
        /// <returns>The measurements.</returns>
        public IReadOnlyList<CrudMeasurement> Snapshot()
        {
            return [.. Instruments.SelectMany(Snapshot)];
        }

        /// <summary>Forgets every measurement collected so far.</summary>
        public void Clear()
        {
            foreach (MetricCollector<long> collector in _counts.Values)
            {
                collector.Clear();
            }

            foreach (MetricCollector<double> collector in _durations.Values)
            {
                collector.Clear();
            }
        }

        /// <summary>
        ///     Every measurement collected so far that leaves its instrument's shape: a tag it must carry
        ///     missing, a tag outside its shape, a closed tag's value outside its set, an open tag's
        ///     value empty, or a negative value.
        /// </summary>
        /// <returns>One line per violation; empty when every measurement holds.</returns>
        public IReadOnlyList<string> Violations()
        {
            List<string> violations = [];
            foreach (CrudMeasurement measurement in Snapshot())
            {
                Shape shape = Shapes[measurement.Instrument];

                // --- The value: a count, a number of rounds or entities, or a duration ---
                if (measurement.Value < 0)
                {
                    violations.Add($"{measurement}: a negative value.");
                }

                // --- The tags it must carry, each within its set ---
                foreach ((string tag, FrozenSet<string>? values) in shape.Required)
                {
                    if (measurement.Tag(tag) is not string value)
                    {
                        violations.Add($"{measurement}: no {tag} tag.");
                    }
                    else if (!Admits(values, value))
                    {
                        violations.Add($"{measurement}: {tag} '{value}' is outside its closed set.");
                    }
                }

                // --- The tags it may carry, and no other ---
                foreach (string tag in measurement.Tags.Keys)
                {
                    if (shape.Required.ContainsKey(tag))
                    {
                        continue;
                    }

                    if (!shape.Optional.TryGetValue(tag, out FrozenSet<string>? values))
                    {
                        violations.Add($"{measurement}: the tag {tag} is not one the instrument carries.");
                    }
                    else if (!Admits(values, measurement.Tag(tag)))
                    {
                        violations.Add($"{measurement}: {tag} '{measurement.Tag(tag)}' is outside its closed set.");
                    }
                }
            }

            return violations;
        }

        /// <summary>Fails the test when any measurement collected so far leaves its instrument's shape.</summary>
        public void AssertClosed()
        {
            IReadOnlyList<string> violations = Violations();
            if (violations.Count > 0)
            {
                Assert.Fail("The pipeline's instruments recorded measurements outside their closed tag sets:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (MetricCollector<long> collector in _counts.Values)
            {
                collector.Dispose();
            }

            foreach (MetricCollector<double> collector in _durations.Values)
            {
                collector.Dispose();
            }
        }

        /// <summary>Whether a tag's set admits a value: any non-empty text for an open set.</summary>
        /// <param name="values">The closed set; null for an open one.</param>
        /// <param name="value">The value.</param>
        /// <returns>True when admitted.</returns>
        private static bool Admits(FrozenSet<string>? values, string? value)
        {
            return !string.IsNullOrEmpty(value) && (values is null || values.Contains(value));
        }

        /// <summary>A shape's tags, by name.</summary>
        /// <param name="tags">Each tag and its closed set, or null for an open one.</param>
        /// <returns>The tags.</returns>
        private static FrozenDictionary<string, FrozenSet<string>?> Tags(params (string Tag, FrozenSet<string>? Values)[] tags)
        {
            return tags.ToFrozenDictionary(static tag => tag.Tag, static tag => tag.Values, StringComparer.Ordinal);
        }

        /// <summary>An instrument's shape.</summary>
        /// <param name="Required">The tags every measurement carries.</param>
        /// <param name="Optional">The tags a measurement may carry beside them.</param>
        private sealed record Shape(FrozenDictionary<string, FrozenSet<string>?> Required, FrozenDictionary<string, FrozenSet<string>?> Optional);
    }
}
