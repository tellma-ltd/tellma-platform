// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     The names of the CRUD pipeline's meter, instruments and tags. Every tag has a closed set of
    ///     values; none carries an entity, a tenant or a user, which belong to the request span and the
    ///     structured log.
    /// </summary>
    public static class CrudTelemetryNames
    {
        /// <summary>The meter every instrument below is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>Counter of operations, by operation, outcome and source.</summary>
        public const string Operations = "tellma.crud.operations";

        /// <summary>Histogram of operation durations in seconds, by operation, outcome, source and lane.</summary>
        public const string OperationDuration = "tellma.crud.operation.duration";

        /// <summary>Histogram of the validation rounds an operation dispatched, by operation and outcome.</summary>
        public const string ValidationRounds = "tellma.crud.validation.rounds";

        /// <summary>Histogram of the entities one save carried, by source and lane.</summary>
        public const string SaveEntities = "tellma.crud.save.entities";

        /// <summary>Counter of concurrency conflicts, by kind.</summary>
        public const string ConcurrencyConflicts = "tellma.crud.concurrency.conflicts";

        /// <summary>Counter of saves that skipped the stamp comparison.</summary>
        public const string ConcurrencyOverrides = "tellma.crud.concurrency.overrides";

        /// <summary>Counter of post-commit failures, by effect.</summary>
        public const string EffectFailures = "tellma.crud.effects.failures";

        /// <summary>Counter of operations re-run after a stale-context refusal, by tag.</summary>
        public const string StaleContextReruns = "tellma.crud.stale_context.reruns";

        /// <summary>Histogram of the seconds a query's argument discovery took on a cache miss.</summary>
        public const string QueryDiscoverDuration = "tellma.crud.query.discover.duration";

        /// <summary>Counter of enlisted writes, by the host's operation, kind and outcome.</summary>
        public const string Enlistments = "tellma.crud.enlistments";

        /// <summary>Counter of warnings raised for confirmation, by key and outcome.</summary>
        public const string Confirmations = "tellma.crud.confirmations";

        /// <summary>Tag: the operation (<c>query</c>, <c>save</c>, <c>action</c>, …).</summary>
        public const string OperationTag = "crud.operation";

        /// <summary>Tag: the outcome (<c>ok</c>, <c>validation</c>, <c>forbidden</c>, …).</summary>
        public const string OutcomeTag = "crud.outcome";

        /// <summary>Tag: the source of a write.</summary>
        public const string SourceTag = "crud.source";

        /// <summary>Tag: the lane, <c>small</c> or <c>large</c>, by the operation's payload rows.</summary>
        public const string LaneTag = "crud.lane";

        /// <summary>Tag: the kind of a conflict or an enlistment.</summary>
        public const string KindTag = "crud.kind";

        /// <summary>Tag: the effect component's type name.</summary>
        public const string EffectTag = "crud.effect";

        /// <summary>Tag: the stale version tag (<c>permissions</c>, <c>settings</c>, <c>entity</c>) or <c>guard</c>.</summary>
        public const string TagTag = "crud.tag";

        /// <summary>Tag: a confirmation's declared key.</summary>
        public const string KeyTag = "crud.key";
    }
}
