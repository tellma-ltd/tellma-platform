// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §9: the worker owns one activity per handler invocation, `process <key>`, a true
    // root linked to every claimed row's enqueuing trace, and the completion is its child Client
    // span `settle <key>`. Tags are bounded: the messaging system, the key, the operation, the batch
    // size and the attempt; tenant, job and user ids go to the log scope, never to tags.
    /// <summary>The spans the job machinery starts, and the trace id an outcome records.</summary>
    internal static class JobsActivities
    {
        /// <summary>The <c>messaging.system</c> of every job span.</summary>
        public const string MessagingSystem = "tellma.jobs";

        /// <summary>The operation of a handler invocation's span.</summary>
        public const string ProcessOperation = "process";

        /// <summary>The operation of a completion's span.</summary>
        public const string SettleOperation = "settle";

        /// <summary>The tag naming the messaging system.</summary>
        public const string SystemTag = "messaging.system";

        /// <summary>The tag naming the handler key.</summary>
        public const string DestinationTag = "messaging.destination.name";

        /// <summary>The tag naming the operation.</summary>
        public const string OperationTag = "messaging.operation.name";

        /// <summary>The tag counting the jobs of the span.</summary>
        public const string MessageCountTag = "messaging.batch.message_count";

        /// <summary>The tag carrying the attempt of the span's first job.</summary>
        public const string AttemptTag = "tellma.jobs.attempt";

        /// <summary>The source of the job spans: Core's.</summary>
        public static ActivitySource Source { get; } = new(JobsTelemetryNames.ActivitySourceName);

        /// <summary>
        ///     Starts <c>process &lt;key&gt;</c> as a true root: the ambient activity is cleared first,
        ///     so the enqueuing trace neither parents the invocation nor decides its sampling; each
        ///     job's own trace parent becomes a link instead.
        /// </summary>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="jobs">The partition's jobs, in claim order.</param>
        /// <returns>The activity; null when nothing listens.</returns>
        public static Activity? StartProcess(string handlerKey, IReadOnlyList<Job> jobs)
        {
            ArgumentNullException.ThrowIfNull(handlerKey);
            ArgumentNullException.ThrowIfNull(jobs);

            // --- The links: one per job whose enqueue recorded a parseable trace parent ---
            List<ActivityLink> links = [];
            foreach (Job job in jobs)
            {
                if (job.TraceParent is string parent && ActivityContext.TryParse(parent, traceState: null, out ActivityContext context))
                {
                    links.Add(new ActivityLink(context));
                }
            }

            // --- The root: no parent, the bounded tags, the links ---
            Activity.Current = null;
            return Source.StartActivity(
                $"{ProcessOperation} {handlerKey}",
                ActivityKind.Consumer,
                parentContext: default,
                Tags(handlerKey, ProcessOperation, jobs),
                links);
        }

        /// <summary>Starts <c>settle &lt;key&gt;</c>, the completion's span, as a child of the current activity.</summary>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="jobs">The jobs the completion records, in claim order.</param>
        /// <returns>The activity; null when nothing listens.</returns>
        public static Activity? StartSettle(string handlerKey, IReadOnlyList<Job> jobs)
        {
            ArgumentNullException.ThrowIfNull(handlerKey);
            ArgumentNullException.ThrowIfNull(jobs);
            return Source.StartActivity($"{SettleOperation} {handlerKey}", ActivityKind.Client, parentContext: default, Tags(handlerKey, SettleOperation, jobs));
        }

        /// <summary>The trace id an outcome records: the current activity's, as 32 hex digits; null when no activity is current.</summary>
        /// <returns>The trace id, or null.</returns>
        public static string? CurrentTraceId()
        {
            return Activity.Current is Activity current && current.TraceId != default ? current.TraceId.ToHexString() : null;
        }

        /// <summary>The bounded tags of a job span.</summary>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="operation">The operation.</param>
        /// <param name="jobs">The span's jobs.</param>
        /// <returns>The tags.</returns>
        private static List<KeyValuePair<string, object?>> Tags(string handlerKey, string operation, IReadOnlyList<Job> jobs)
        {
            return
            [
                new(SystemTag, MessagingSystem),
                new(DestinationTag, handlerKey),
                new(OperationTag, operation),
                new(MessageCountTag, jobs.Count),
                new(AttemptTag, jobs.Count > 0 ? jobs[0].Attempts : 0),
            ];
        }
    }
}
