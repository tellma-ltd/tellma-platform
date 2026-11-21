// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Abstractions.Tests.Jobs
{
    /// <summary>
    ///     The rest of the job contract: the job row's mapping and stack, the batch a handler receives,
    ///     messages and errors compared by content, the failure exception's guard, and the pinned
    ///     status and telemetry names.
    /// </summary>
    public class JobContractTests
    {
        [Fact]
        public void The_job_row_maps_to_core_jobs_and_serves_reads_alone()
        {
            TableAttribute table = typeof(Job).GetCustomAttribute<TableAttribute>()!;

            Assert.Equal(("Jobs", "core"), (table.Name, table.Schema));
            Assert.NotNull(typeof(Job).GetCustomAttribute<TableTypeAttribute>());
            Assert.Equal(StackOperations.Query | StackOperations.Details, typeof(Job).GetCustomAttribute<StackAttribute>()!.Operations);
            Assert.True(typeof(Job).IsSealed);
        }

        [Fact]
        public void Every_member_of_the_job_row_is_server_owned()
        {
            PropertyInfo[] properties = typeof(Job).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.NotEmpty(properties);
            Assert.All(properties, static property => Assert.NotNull(property.GetCustomAttribute<ServerOwnedAttribute>()));
        }

        [Fact]
        public void The_default_and_related_selections_name_job_members_and_leave_out_the_internal_ones()
        {
            string[] defaults = typeof(Job).GetCustomAttribute<DefaultSelectAttribute>()!.Select.Split(',');
            string[] related = typeof(Job).GetCustomAttribute<RelatedSelectAttribute>()!.Select.Split(',');

            Assert.All(defaults.Concat(related), static name => Assert.NotNull(typeof(Job).GetProperty(name)));
            Assert.Subset(defaults.ToHashSet(), related.ToHashSet());

            // The arguments, the lease owner, the trace parent and the pending confirmations are read
            // by the job's page or the worker, never by a list.
            Assert.DoesNotContain(nameof(Job.ArgumentsJson), defaults);
            Assert.DoesNotContain(nameof(Job.LeaseOwner), defaults);
            Assert.DoesNotContain(nameof(Job.TraceParent), defaults);
            Assert.DoesNotContain(nameof(Job.PendingConfirmationsJson), defaults);
        }

        [Fact]
        public void The_statuses_are_pinned_by_name()
        {
            // Stored by name, so a rename is a data migration.
            Assert.Equal(
                ["Pending", "Running", "Succeeded", "Failed", "Cancelled", "Held", "PendingConfirmation"],
                Enum.GetNames<JobStatus>());
            Assert.Equal(
                ["None", "LeaseLost", "CancelRequested", "HostStopping"],
                Enum.GetNames<JobCancellationReason>());
        }

        [Fact]
        public void A_batch_holds_at_least_one_item_and_copies_the_list()
        {
            JobItem<Job> first = JobTestItems.Create();
            List<JobItem<Job>> items = [first];

            JobBatch<Job> batch = new() { Items = items, Batch = JobTestItems.UnusedBatch() };
            items.Add(JobTestItems.Create());

            Assert.Same(first, Assert.Single(batch.Items));
            _ = Assert.Throws<ArgumentException>(static () => new JobBatch<Job> { Items = [], Batch = JobTestItems.UnusedBatch() });
            _ = Assert.Throws<ArgumentException>(static () => new JobBatch<Job> { Items = [null!], Batch = JobTestItems.UnusedBatch() });
            _ = Assert.Throws<ArgumentNullException>(static () => new JobBatch<Job> { Items = null!, Batch = JobTestItems.UnusedBatch() });
            _ = Assert.Throws<ArgumentNullException>(static () => new JobBatch<Job> { Items = [JobTestItems.Create()], Batch = null! });
        }

        [Fact]
        public void Single_serves_a_batch_of_one_and_refuses_a_larger_one()
        {
            JobItem<Job> only = JobTestItems.Create();

            Assert.Same(only, new JobBatch<Job> { Items = [only], Batch = JobTestItems.UnusedBatch() }.Single);

            JobBatch<Job> two = new() { Items = [JobTestItems.Create(), JobTestItems.Create()], Batch = JobTestItems.UnusedBatch() };
            Assert.Contains("2 items", Assert.Throws<InvalidOperationException>(() => two.Single).Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Messages_compare_by_key_and_argument_content()
        {
            Dictionary<string, object?> arguments = new() { ["count"] = 3 };
            JobMessage message = new("Export.Progress", arguments);
            arguments["count"] = 4;

            Assert.Equal(new JobMessage("Export.Progress", new Dictionary<string, object?> { ["count"] = 3 }), message);
            Assert.Equal(message.GetHashCode(), new JobMessage("Export.Progress", new Dictionary<string, object?> { ["count"] = 3 }).GetHashCode());
            Assert.NotEqual(new JobMessage("Export.Progress", arguments), message);
            Assert.NotEqual(new JobMessage("Export.Started", new Dictionary<string, object?> { ["count"] = 3 }), message);
            Assert.Empty(new JobMessage("Export.Started").Arguments);
            _ = Assert.Throws<ArgumentException>(static () => new JobMessage(" "));
            _ = Assert.Throws<ArgumentNullException>(static () => new JobMessage("Export.Started", null!));
        }

        [Fact]
        public void Errors_are_coded_errors_compared_by_content()
        {
            JobError error = new("Export.TooLarge", new Dictionary<string, object?> { ["rows"] = 12 });

            Assert.IsAssignableFrom<CodedError>(error);
            Assert.Equal(new JobError("Export.TooLarge", new Dictionary<string, object?> { ["rows"] = 12 }), error);
            Assert.NotEqual(new JobError("Export.TooLarge"), error);
            _ = Assert.Throws<ArgumentException>(static () => new JobError(""));
        }

        [Fact]
        public void The_failure_exception_carries_its_error_and_its_serialised_arguments()
        {
            JobError error = new("Export.TooLarge", new Dictionary<string, object?> { ["rows"] = 12 });

            JobFailedException exception = new(error);

            Assert.Same(error, exception.Error);
            Assert.Equal(/*lang=json,strict*/ """{"rows":12}""", exception.ArgumentsJson);
            Assert.Contains("'Export.TooLarge'", exception.Message, StringComparison.Ordinal);
            Assert.Null(new JobFailedException(new JobError("Export.Cancelled")).ArgumentsJson);
        }

        [Fact]
        public void The_failure_exception_refuses_an_error_too_wide_for_the_job_table()
        {
            _ = Assert.Throws<ArgumentNullException>(static () => new JobFailedException(null!));
            _ = new JobFailedException(JobTestItems.ErrorOfLength(JobLimits.MaxArgumentsLength));

            ArgumentException exception = Assert.Throws<ArgumentException>(
                static () => new JobFailedException(JobTestItems.ErrorOfLength(JobLimits.MaxArgumentsLength + 1)));
            Assert.Equal("error", exception.ParamName);
            Assert.Equal("error", Assert.Throws<ArgumentException>(static () => new JobFailedException(new JobError(new string('E', 65)))).ParamName);
        }

        [Fact]
        public void The_limits_are_pinned()
        {
            Assert.Equal(
                (64, 4000, 500, 15, (byte)100, TimeSpan.FromDays(30)),
                (JobLimits.MaxKeyLength, JobLimits.MaxArgumentsLength, JobLimits.MaxBatchSize, JobLimits.MinLeaseSeconds,
                    JobLimits.MaxProgressPercent, JobLimits.MaxRetryDelay));
        }

        [Fact]
        public void The_telemetry_names_are_pinned()
        {
            Assert.Equal(
                [
                    "Tellma.Core", "Tellma.Core",
                    "tellma.jobs.claimed", "tellma.jobs.completed", "tellma.jobs.duration", "tellma.jobs.queue.latency",
                    "tellma.jobs.batch_fill", "tellma.jobs.in_flight", "tellma.jobs.lease_lost", "tellma.jobs.backlog.age",
                    "tellma.jobs.poll.duration", "tellma.jobs.orphaned",
                    "tellma.schedules.fired", "tellma.schedules.missed", "tellma.schedules.overlap_skipped", "tellma.schedules.gap_detected",
                    "handler", "outcome", "policy",
                ],
                [
                    JobsTelemetryNames.MeterName, JobsTelemetryNames.ActivitySourceName,
                    JobsTelemetryNames.Claimed, JobsTelemetryNames.Completed, JobsTelemetryNames.Duration, JobsTelemetryNames.QueueLatency,
                    JobsTelemetryNames.BatchFill, JobsTelemetryNames.InFlight, JobsTelemetryNames.LeaseLost, JobsTelemetryNames.BacklogAge,
                    JobsTelemetryNames.PollDuration, JobsTelemetryNames.Orphaned,
                    JobsTelemetryNames.SchedulesFired, JobsTelemetryNames.SchedulesMissed, JobsTelemetryNames.SchedulesOverlapSkipped,
                    JobsTelemetryNames.SchedulesGapDetected,
                    JobsTelemetryNames.HandlerTag, JobsTelemetryNames.OutcomeTag, JobsTelemetryNames.PolicyTag,
                ]);
        }
    }
}
