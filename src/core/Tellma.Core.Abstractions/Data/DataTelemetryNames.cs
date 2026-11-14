// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The names the data layer's telemetry is published under: the instruments of the
    ///     <c>Tellma.Core</c> meter, their tags, and the attributes of the round-trip span. No
    ///     instrument carries a tenant or user tag; those go to traces and logs only.
    /// </summary>
    public static class DataTelemetryNames
    {
        /// <summary>The meter every data-layer instrument belongs to.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>The activity source the round-trip spans are started from.</summary>
        public const string ActivitySourceName = "Tellma.Core";

        /// <summary>Round trips per scope (histogram).</summary>
        public const string RoundTrips = "tellma.data.roundtrips";

        /// <summary>The duration of one round trip, in seconds (histogram).</summary>
        public const string RoundTripDuration = "tellma.data.roundtrip.duration";

        /// <summary>Statements per batch (histogram).</summary>
        public const string Statements = "tellma.data.batch.statements";

        /// <summary>Round trips re-run after a failure (counter).</summary>
        public const string Retries = "tellma.data.retries";

        /// <summary>Commit probes after an ambiguous failure (counter).</summary>
        public const string CommitProbes = "tellma.data.commit.probes";

        /// <summary>Id-buffer refills (counter).</summary>
        public const string IdRefills = "tellma.data.ids.refills";

        /// <summary>Sequences healed past a gap (counter).</summary>
        public const string IdHealed = "tellma.data.ids.healed";

        /// <summary>Ids reserved from a sequence (counter).</summary>
        public const string IdReserved = "tellma.data.ids.reserved";

        /// <summary>Ids returned to the buffer (counter).</summary>
        public const string IdReturned = "tellma.data.ids.returned";

        /// <summary>Rows saved (counter).</summary>
        public const string RowsSaved = "tellma.data.rows.saved";

        /// <summary>Rows deleted (counter).</summary>
        public const string RowsDeleted = "tellma.data.rows.deleted";

        /// <summary>Concurrency conflicts (counter).</summary>
        public const string ConcurrencyConflicts = "tellma.data.concurrency.conflicts";

        /// <summary>Tree recompute statements (counter).</summary>
        public const string TreeRecomputes = "tellma.data.tree.recomputes";

        /// <summary>Tree rows repaired (counter).</summary>
        public const string TreeRepairs = "tellma.data.tree.repairs";

        /// <summary>The batch-purpose tag.</summary>
        public const string PurposeTag = "purpose";

        /// <summary>The outcome tag.</summary>
        public const string OutcomeTag = "outcome";

        /// <summary>The operation tag: the scope's <see cref="DataAccessScope.Operation" />.</summary>
        public const string OperationTag = "operation";

        /// <summary>The entity tag.</summary>
        public const string EntityTag = "entity";

        /// <summary>The failure-class tag.</summary>
        public const string ClassTag = "class";

        /// <summary>The error tag.</summary>
        public const string ErrorTag = "error";

        /// <summary>The sequence tag.</summary>
        public const string SequenceTag = "sequence";

        /// <summary>The database role of a SQL span (<c>tenant</c> or <c>catalog</c>); a span attribute, never a metric tag.</summary>
        public const string RoleTag = "tellma.db.role";

        /// <summary>The span attribute naming the batch's purpose, in lower case.</summary>
        public const string PurposeAttribute = "tellma.data.purpose";

        /// <summary>The span attribute naming the scope's operation.</summary>
        public const string OperationAttribute = "tellma.data.operation";

        /// <summary>The span attribute numbering the attempt, from 1.</summary>
        public const string AttemptAttribute = "tellma.data.attempt";

        /// <summary>The span attribute holding the tenant id; a trace and log value, never a metric tag.</summary>
        public const string TenantIdAttribute = "tellma.tenant.id";

        /// <summary>The value of <see cref="RoleTag" /> for a tenant database.</summary>
        public const string TenantRole = "tenant";
    }
}
