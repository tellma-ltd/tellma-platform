// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Caching
{
    // The instruments and events are spec 0013 §11's version-tag rows.
    /// <summary>
    ///     The version tags' instruments on the core meter (bumps, stale results, missing rows) and the
    ///     once-per-process warning of a registered tag the database lacks. No instrument carries a
    ///     tenant, a user or a tag's name; those are log properties.
    /// </summary>
    internal sealed class VersionTagMetrics
    {
        /// <summary>The kind of the <c>settings</c> tag.</summary>
        public const string SettingsKind = "settings";

        /// <summary>The kind of the <c>permissions</c> tag.</summary>
        public const string PermissionsKind = "permissions";

        /// <summary>The kind of the <c>entities</c> tag.</summary>
        public const string EntitiesKind = "entities";

        /// <summary>The kind of an <c>entity:*</c> tag.</summary>
        public const string EntityKind = "entity";

        /// <summary>The kind of a pack's tag.</summary>
        public const string OtherKind = "other";

        /// <summary>The kind of a user-level tag.</summary>
        public const string UserKind = "user";

        /// <summary>The stale phase of a read or validate batch whose re-run dependency moved.</summary>
        public const string ReadRerunPhase = "read-rerun";

        /// <summary>The stale phase of a persist batch the in-transaction guard refused.</summary>
        public const string WriteGuardPhase = "write-guard";

        /// <summary>The stale phase of a request whose one re-run moved again.</summary>
        public const string ExhaustedPhase = "exhausted";

        /// <summary>The names already warned about as missing, once per process.</summary>
        private readonly ConcurrentDictionary<string, bool> _warnedMissing = new(StringComparer.Ordinal);

        /// <summary>Bumps.</summary>
        private readonly Counter<long> _bumps;

        /// <summary>Stale results.</summary>
        private readonly Counter<long> _stale;

        /// <summary>Bumps of names the database lacks.</summary>
        private readonly Counter<long> _missing;

        /// <summary>The logger of the missing-row warnings and the stale re-runs.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        /// <param name="logger">The logger.</param>
        public VersionTagMetrics(IMeterFactory meterFactory, ILogger<VersionTagMetrics> logger)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            ArgumentNullException.ThrowIfNull(logger);

            Meter meter = meterFactory.Create(CacheTelemetryNames.MeterName);
            _logger = logger;

            _bumps = meter.CreateCounter<long>(
                CacheTelemetryNames.Bumps,
                "{bump}",
                "Version tags moved by committed batches, by tag kind.");

            _stale = meter.CreateCounter<long>(
                CacheTelemetryNames.Stale,
                "{event}",
                "Results discarded because a version tag they depended on moved, by phase.");

            _missing = meter.CreateCounter<long>(
                CacheTelemetryNames.Missing,
                "{name}",
                "Bumps of version tags the tenant database has no row for, by tag kind.");
        }

        /// <summary>The kind of a tenant-level tag name, for the instruments.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The kind.</returns>
        public static string KindOf(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name switch
            {
                VersionTagNames.Settings => SettingsKind,
                VersionTagNames.Permissions => PermissionsKind,
                VersionTagNames.Entities => EntitiesKind,
                _ when name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal) => EntityKind,
                _ => OtherKind,
            };
        }

        /// <summary>Records the bumps of a committed batch.</summary>
        /// <param name="tenantNames">The tenant-level names it moved.</param>
        /// <param name="userTags">How many user-level tags it moved, one per user and column.</param>
        public void RecordBumps(IReadOnlyList<string> tenantNames, int userTags)
        {
            ArgumentNullException.ThrowIfNull(tenantNames);
            foreach (string name in tenantNames)
            {
                _bumps.Add(1, new KeyValuePair<string, object?>(CacheTelemetryNames.TagKindTag, KindOf(name)));
            }

            if (userTags > 0)
            {
                _bumps.Add(userTags, new KeyValuePair<string, object?>(CacheTelemetryNames.TagKindTag, UserKind));
            }
        }

        /// <summary>Records one stale result.</summary>
        /// <param name="phase"><see cref="ReadRerunPhase" />, <see cref="WriteGuardPhase" /> or <see cref="ExhaustedPhase" />.</param>
        public void RecordStale(string phase)
        {
            _stale.Add(1, new KeyValuePair<string, object?>(CacheTelemetryNames.StalePhaseTag, phase));
        }

        /// <summary>Records the bumps a batch attempted of names the database has no row for.</summary>
        /// <param name="names">The names.</param>
        public void RecordMissing(IEnumerable<string> names)
        {
            ArgumentNullException.ThrowIfNull(names);
            foreach (string name in names)
            {
                _missing.Add(1, new KeyValuePair<string, object?>(CacheTelemetryNames.TagKindTag, KindOf(name)));
            }
        }

        /// <summary>
        ///     Warns, once per name for the life of the process, about each registered name a prelude
        ///     did not find: the kind behind it stays uncacheable on this instance until the migrator
        ///     seeds its row.
        /// </summary>
        /// <param name="snapshot">The tags the prelude read.</param>
        /// <param name="registered">Every registered name.</param>
        public void WarnMissing(VersionTagSnapshot snapshot, IReadOnlyList<string> registered)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(registered);
            foreach (string name in registered)
            {
                if (!snapshot.Has(name) && _warnedMissing.TryAdd(name, true))
                {
                    CachingLog.VersionTagRowMissing(_logger, name, snapshot.TenantId);
                }
            }
        }

        /// <summary>Logs and meters a read or validate batch discarded because a re-run dependency moved.</summary>
        /// <param name="names">The moved names.</param>
        /// <param name="tenantId">The tenant.</param>
        public void ReportReadRerun(IReadOnlyList<string> names, int tenantId)
        {
            ArgumentNullException.ThrowIfNull(names);
            RecordStale(ReadRerunPhase);
            // The names are joined only when the message is written.
            if (_logger.IsEnabled(LogLevel.Information))
            {
                string joined = string.Join(", ", names);
                CachingLog.StaleVersionTagRetry(_logger, joined, tenantId);
            }
        }
    }
}
