// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The web API's defaults, bound from <c>Tellma:Api</c>: values a distribution never needs to
    ///     set.
    /// </summary>
    public sealed class ApiOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Api";

        /// <summary>The longest a request may be allowed to run: a day, far beyond any request a browser waits for.</summary>
        internal static readonly TimeSpan MaxTimeout = TimeSpan.FromDays(1);

        /// <summary>
        ///     The client names the <c>Tellma-Client</c> header may carry; <c>web</c> by default. A
        ///     closed set: the name is the request context's client and a metric tag.
        /// </summary>
        public IReadOnlySet<string> ClientNames { get; set; } = new HashSet<string>(StringComparer.Ordinal) { "web" };

        /// <summary>The base every problem's <c>type</c> URI is formed from, ending in <c>/</c>.</summary>
        public string ProblemTypeBase { get; set; } = "https://tellma.com/problems/";

        /// <summary>
        ///     The largest JSON body a web endpoint reads, in bytes; 8 MB by default. A larger body is
        ///     refused before it is read.
        /// </summary>
        public long MaxJsonBodyBytes { get; set; } = 8 * 1024 * 1024;

        /// <summary>
        ///     The most entities one web save carries; 1,000 by default. It lowers a stack's own save
        ///     ceiling, the smaller of the two winning; a larger set goes through an import.
        /// </summary>
        public int MaxEntitiesPerSave { get; set; } = 1000;

        /// <summary>The requests one user makes to one tenant per minute, on each instance; 600 by default.</summary>
        public int RequestsPerMinutePerUser { get; set; } = 600;

        /// <summary>
        ///     The requests one user makes to every tenant together per minute, on each instance; 1,200
        ///     by default. It bounds the rate, and the limiter state, of one account that spreads its
        ///     requests over many tenant ids; at least <see cref="RequestsPerMinutePerUser" />.
        /// </summary>
        public int RequestsPerMinutePerUserAcrossTenants { get; set; } = 1200;

        /// <summary>
        ///     The requests one tenant runs at once on each instance; 64 by default. A burst on one
        ///     tenant cannot exhaust the instance for the others, and the tenant's connection pool waits
        ///     stay bounded.
        /// </summary>
        public int ConcurrentRequestsPerTenant { get; set; } = 64;

        /// <summary>The anonymous requests one client address makes per minute, on each instance; 60 by default.</summary>
        public int AnonymousRequestsPerMinutePerIp { get; set; } = 60;

        /// <summary>The exports one user runs at once in one tenant, on each instance; 2 by default.</summary>
        public int ConcurrentExportsPerUser { get; set; } = 2;

        /// <summary>The imports one user runs at once in one tenant, on each instance; 1 by default.</summary>
        public int ConcurrentImportsPerUser { get; set; } = 1;

        /// <summary>The blob reads one tenant runs at once on each instance; 32 by default.</summary>
        public int ConcurrentBlobReadsPerTenant { get; set; } = 32;

        /// <summary>The blob reads of one tenant that wait for a running one to finish; 256 by default.</summary>
        public int BlobReadQueuePerTenant { get; set; } = 256;

        /// <summary>The reports one user runs at once in one tenant in the interactive lane; 4 by default.</summary>
        public int ConcurrentReportsPerUser { get; set; } = 4;

        /// <summary>The interactive reports of one user that wait for a running one to finish; 8 by default.</summary>
        public int ReportQueuePerUser { get; set; } = 8;

        /// <summary>
        ///     The reports one user runs at once in one tenant in the batch lane, which a request
        ///     carrying <c>Tellma-Priority: low</c> takes; 6 by default.
        /// </summary>
        public int ConcurrentBatchReportsPerUser { get; set; } = 6;

        /// <summary>The batch-lane reports of one user that wait for a running one to finish; 24 by default.</summary>
        public int BatchReportQueuePerUser { get; set; } = 24;

        /// <summary>The client telemetry batches one user sends per minute, on each instance; 60 by default.</summary>
        public int TelemetryBatchesPerMinutePerUser { get; set; } = 60;

        /// <summary>How long an ordinary web, blob or hub request may run; 30 seconds by default.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>How long an operation that runs long, such as an export, an import or a report, may run; 5 minutes by default.</summary>
        public TimeSpan LongRequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>Whether JSON responses are compressed with Brotli or Gzip; on by default.</summary>
        public bool EnableCompression { get; set; } = true;

        /// <summary>
        ///     Whether a web API response carries <c>Server-Timing: db;dur=&lt;ms&gt;;desc="&lt;n&gt; calls"</c>,
        ///     the database's share of the request; on by default in Development alone, since it tells a
        ///     caller how long the server spent in the database.
        /// </summary>
        public bool ServerTiming { get; set; }

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The client names: a closed set of lowercase words ---
            if (ClientNames is null || ClientNames.Count == 0)
            {
                yield return new ValidationResult($"{SectionPath}:{nameof(ClientNames)} is empty; the SPA's name 'web' at least is required.", [nameof(ClientNames)]);
            }
            else
            {
                foreach (string name in ClientNames)
                {
                    if (!IsClientName(name))
                    {
                        yield return new ValidationResult(
                            $"{SectionPath}:{nameof(ClientNames)} holds '{name}'; a client name is 1 to 32 lowercase letters, digits or hyphens.",
                            [nameof(ClientNames)]);
                    }
                }
            }

            // --- The body and save ceilings: positive, or nothing could be sent ---
            if (MaxJsonBodyBytes <= 0)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(MaxJsonBodyBytes)} is {MaxJsonBodyBytes}; it must be positive.",
                    [nameof(MaxJsonBodyBytes)]);
            }

            if (MaxEntitiesPerSave <= 0)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(MaxEntitiesPerSave)} is {MaxEntitiesPerSave}; it must be positive.",
                    [nameof(MaxEntitiesPerSave)]);
            }

            // --- The rate and concurrency limits: positive, or every request would be refused; queues may be empty ---
            foreach ((string name, int value) in Positive())
            {
                if (value <= 0)
                {
                    yield return new ValidationResult($"{SectionPath}:{name} is {value}; it must be positive.", [name]);
                }
            }

            foreach ((string name, int value) in Queues())
            {
                if (value < 0)
                {
                    yield return new ValidationResult($"{SectionPath}:{name} is {value}; it must not be negative.", [name]);
                }
            }

            // --- The cap across tenants: no lower than one tenant's, which it would otherwise undercut ---
            if (RequestsPerMinutePerUserAcrossTenants < RequestsPerMinutePerUser)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(RequestsPerMinutePerUserAcrossTenants)} is {RequestsPerMinutePerUserAcrossTenants}; it must be at least {nameof(RequestsPerMinutePerUser)}, {RequestsPerMinutePerUser}.",
                    [nameof(RequestsPerMinutePerUserAcrossTenants)]);
            }

            // --- The timeouts: positive, bounded, and the long one no shorter than the ordinary one ---
            if (RequestTimeout <= TimeSpan.Zero)
            {
                yield return new ValidationResult($"{SectionPath}:{nameof(RequestTimeout)} is {RequestTimeout}; it must be positive.", [nameof(RequestTimeout)]);
            }

            if (LongRequestTimeout > MaxTimeout)
            {
                yield return new ValidationResult($"{SectionPath}:{nameof(LongRequestTimeout)} is {LongRequestTimeout}; it must be at most {MaxTimeout}.", [nameof(LongRequestTimeout)]);
            }

            if (LongRequestTimeout < RequestTimeout)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(LongRequestTimeout)} is {LongRequestTimeout}; it must be at least {nameof(RequestTimeout)}, {RequestTimeout}.",
                    [nameof(LongRequestTimeout)]);
            }

            // --- The problem type base: an absolute URI every code is appended to ---
            if (string.IsNullOrWhiteSpace(ProblemTypeBase) || !ProblemTypeBase.EndsWith('/')
                || !Uri.TryCreate(ProblemTypeBase, UriKind.Absolute, out _))
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(ProblemTypeBase)} is '{ProblemTypeBase}'; it must be an absolute URI ending in '/'.",
                    [nameof(ProblemTypeBase)]);
            }
        }

        /// <summary>The limits that must be positive, by name.</summary>
        /// <returns>Each limit's name and value.</returns>
        private (string Name, int Value)[] Positive()
        {
            return
            [
                (nameof(RequestsPerMinutePerUser), RequestsPerMinutePerUser),
                (nameof(RequestsPerMinutePerUserAcrossTenants), RequestsPerMinutePerUserAcrossTenants),
                (nameof(ConcurrentRequestsPerTenant), ConcurrentRequestsPerTenant),
                (nameof(AnonymousRequestsPerMinutePerIp), AnonymousRequestsPerMinutePerIp),
                (nameof(ConcurrentExportsPerUser), ConcurrentExportsPerUser),
                (nameof(ConcurrentImportsPerUser), ConcurrentImportsPerUser),
                (nameof(ConcurrentBlobReadsPerTenant), ConcurrentBlobReadsPerTenant),
                (nameof(ConcurrentReportsPerUser), ConcurrentReportsPerUser),
                (nameof(ConcurrentBatchReportsPerUser), ConcurrentBatchReportsPerUser),
                (nameof(TelemetryBatchesPerMinutePerUser), TelemetryBatchesPerMinutePerUser),
            ];
        }

        /// <summary>The queue lengths, which may be zero, by name.</summary>
        /// <returns>Each queue's name and length.</returns>
        private (string Name, int Value)[] Queues()
        {
            return
            [
                (nameof(BlobReadQueuePerTenant), BlobReadQueuePerTenant),
                (nameof(ReportQueuePerUser), ReportQueuePerUser),
                (nameof(BatchReportQueuePerUser), BatchReportQueuePerUser),
            ];
        }

        /// <summary>Whether a value is a well-formed client name.</summary>
        /// <param name="name">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsClientName(string? name)
        {
            return name is { Length: > 0 and <= 32 } && name.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
        }
    }
}
