// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Abstractions.Tests.Jobs
{
    /// <summary>
    ///     One claimed item: the last mark wins, a retry's delay and every error are checked before
    ///     anything is marked, notifications wait for success, and the cancellation reason only ever
    ///     escalates to a lost lease.
    /// </summary>
    public class JobItemTests
    {
        [Fact]
        public void An_item_starts_unmarked_and_uncancelled()
        {
            JobItem<Job> item = JobTestItems.Create();

            Assert.Null(item.Mark);
            Assert.Equal(JobCancellationReason.None, item.CancellationReason);
            Assert.Empty(item.SuccessNotifications);
        }

        [Fact]
        public void The_last_mark_wins()
        {
            JobItem<Job> item = JobTestItems.Create();
            JobError error = new("Export.Failed");

            item.Fail(error);
            item.Retry(TimeSpan.FromMinutes(1));
            item.Succeed();
            Assert.Same(JobItemMark.Succeeded, item.Mark);

            item.Retry(TimeSpan.FromMinutes(5), error);
            Assert.Equal(new JobItemMark(JobItemMarkKind.Retry, TimeSpan.FromMinutes(5), error, null), item.Mark);

            item.Fail(error);
            Assert.Equal(new JobItemMark(JobItemMarkKind.Failed, null, error, null), item.Mark);
        }

        [Fact]
        public void A_retry_without_a_delay_leaves_the_backoff_to_the_handler_options()
        {
            JobItem<Job> item = JobTestItems.Create();

            item.Retry();

            Assert.Equal(new JobItemMark(JobItemMarkKind.Retry, null, null, null), item.Mark);
        }

        [Fact]
        public void A_retry_delay_is_shortened_to_the_ceiling_and_never_negative()
        {
            JobItem<Job> item = JobTestItems.Create();

            item.Retry(TimeSpan.FromDays(31));
            Assert.Equal(JobLimits.MaxRetryDelay, item.Mark!.RetryAfter);

            item.Retry(TimeSpan.Zero);
            Assert.Equal(TimeSpan.Zero, item.Mark!.RetryAfter);

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => item.Retry(TimeSpan.FromTicks(-1)));
            Assert.Equal("after", exception.ParamName);
            Assert.Equal(TimeSpan.Zero, item.Mark!.RetryAfter);
        }

        [Fact]
        public void An_error_carries_its_serialised_arguments_into_the_mark()
        {
            JobItem<Job> item = JobTestItems.Create();

            item.Fail(new JobError("Export.TooLarge", new Dictionary<string, object?> { ["rows"] = 12, ["limit"] = 10 }));

            Assert.Equal(/*lang=json,strict*/ """{"rows":12,"limit":10}""", item.Mark!.ErrorArgumentsJson);
        }

        [Fact]
        public void Arguments_of_four_thousand_characters_fit_and_one_more_is_refused_before_anything_is_marked()
        {
            JobItem<Job> item = JobTestItems.Create();

            // {"text":"…"} is the text plus eleven characters.
            item.Fail(JobTestItems.ErrorOfLength(JobLimits.MaxArgumentsLength));
            Assert.Equal(JobLimits.MaxArgumentsLength, item.Mark!.ErrorArgumentsJson!.Length);

            item.Succeed();
            JobError wide = JobTestItems.ErrorOfLength(JobLimits.MaxArgumentsLength + 1);
            Assert.Equal("error", Assert.Throws<ArgumentException>(() => item.Fail(wide)).ParamName);
            Assert.Equal("error", Assert.Throws<ArgumentException>(() => item.Retry(error: wide)).ParamName);
            Assert.Same(JobItemMark.Succeeded, item.Mark);
        }

        [Fact]
        public void An_error_code_longer_than_the_column_is_refused()
        {
            JobItem<Job> item = JobTestItems.Create();

            item.Fail(new JobError(new string('E', JobLimits.MaxKeyLength)));
            ArgumentException exception = Assert.Throws<ArgumentException>(() => item.Fail(new JobError(new string('E', JobLimits.MaxKeyLength + 1))));

            Assert.Equal("error", exception.ParamName);
            Assert.Equal(JobLimits.MaxKeyLength, item.Mark!.Error!.Code.Length);
        }

        [Fact]
        public void An_argument_that_cannot_be_serialised_is_refused_as_an_argument_error()
        {
            JobItem<Job> item = JobTestItems.Create();

            // System.Type is one of the types the serializer refuses outright.
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => item.Fail(new JobError("Export.Failed", new Dictionary<string, object?> { ["type"] = typeof(int) })));

            Assert.Equal("error", exception.ParamName);
            Assert.IsType<NotSupportedException>(exception.InnerException);
            Assert.Null(item.Mark);
        }

        [Fact]
        public void Failing_with_no_error_is_refused()
        {
            _ = Assert.Throws<ArgumentNullException>(static () => JobTestItems.Create().Fail(null!));
        }

        [Fact]
        public void Notifications_on_success_queue_in_order_and_a_snapshot_is_a_copy()
        {
            JobItem<Job> item = JobTestItems.Create();
            NotificationRequest first = new("acme.export.ready", [1], null, null, null, null, null);
            NotificationRequest second = first with { RecipientIds = [2] };

            item.NotifyOnSuccess(first);
            IReadOnlyList<NotificationRequest> snapshot = item.SuccessNotifications;
            item.NotifyOnSuccess(second);

            Assert.Equal([first], snapshot);
            Assert.Equal([first, second], item.SuccessNotifications);
            _ = Assert.Throws<ArgumentNullException>(() => item.NotifyOnSuccess(null!));
        }

        [Fact]
        public void The_first_cancellation_reason_sticks_until_the_lease_is_lost()
        {
            JobItem<Job> item = JobTestItems.Create();

            Assert.True(item.TrySetCancellationReason(JobCancellationReason.CancelRequested));
            Assert.False(item.TrySetCancellationReason(JobCancellationReason.HostStopping));
            Assert.Equal(JobCancellationReason.CancelRequested, item.CancellationReason);

            Assert.True(item.TrySetCancellationReason(JobCancellationReason.LeaseLost));
            Assert.False(item.TrySetCancellationReason(JobCancellationReason.CancelRequested));
            Assert.False(item.TrySetCancellationReason(JobCancellationReason.LeaseLost));
            Assert.Equal(JobCancellationReason.LeaseLost, item.CancellationReason);
        }

        [Fact]
        public void A_stopping_host_gives_way_to_a_lost_lease()
        {
            JobItem<Job> item = JobTestItems.Create();

            Assert.True(item.TrySetCancellationReason(JobCancellationReason.HostStopping));
            Assert.True(item.TrySetCancellationReason(JobCancellationReason.LeaseLost));
            Assert.Equal(JobCancellationReason.LeaseLost, item.CancellationReason);
        }

        [Theory]
        [InlineData(JobCancellationReason.None)]
        [InlineData((JobCancellationReason)99)]
        public void A_cancellation_reason_must_name_a_reason(JobCancellationReason reason)
        {
            JobItem<Job> item = JobTestItems.Create();

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => item.TrySetCancellationReason(reason));
            Assert.Equal(JobCancellationReason.None, item.CancellationReason);
        }

        [Fact]
        public void The_required_members_refuse_null()
        {
            _ = Assert.Throws<ArgumentNullException>(static () => new JobItem<Job> { Job = null!, Item = new Job(), Progress = JobTestItems.NoProgress });
            _ = Assert.Throws<ArgumentNullException>(static () => new JobItem<Job> { Job = new Job(), Item = null!, Progress = JobTestItems.NoProgress });
            _ = Assert.Throws<ArgumentNullException>(static () => new JobItem<Job> { Job = new Job(), Item = new Job(), Progress = null! });
        }

        [Fact]
        public void Arguments_read_tolerantly_skipping_unknown_members_and_defaulting_missing_ones()
        {
            JobItem<Job> item = JobTestItems.Create(arguments: /*lang=json,strict*/ """{"rows":5,"addedLater":true}""");

            ExportArguments arguments = item.Arguments<ExportArguments>();

            // Format is required yet missing: it keeps its default rather than failing the read.
            Assert.Equal((null, 5), (arguments.Format, arguments.Rows));
        }

        [Fact]
        public void Positional_arguments_default_a_missing_parameter()
        {
            JobItem<Job> item = JobTestItems.Create(arguments: /*lang=json,strict*/ """{"name":"Q3"}""");

            Assert.Equal(new PositionalArguments("Q3", 0), item.Arguments<PositionalArguments>());
        }

        [Fact]
        public void Arguments_absent_from_the_job_read_as_the_types_empty_shape()
        {
            JobItem<Job> item = JobTestItems.Create();

            Assert.Equal((null, 0), (item.Arguments<ExportArguments>().Format, item.Arguments<ExportArguments>().Rows));
            Assert.NotSame(item.Arguments<ExportArguments>(), item.Arguments<ExportArguments>());
            Assert.Empty(item.Arguments<List<int>>());
            Assert.Empty(item.Arguments<Dictionary<string, int>>());
            Assert.Equal(0, item.Arguments<int>());
            Assert.Null(item.Arguments<int?>());
            Assert.Null(item.Arguments<string>());
        }

        [Fact]
        public void Arguments_that_do_not_fit_the_type_throw_a_json_exception()
        {
            JobItem<Job> item = JobTestItems.Create(arguments: /*lang=json,strict*/ """{"rows":"many"}""");

            _ = Assert.Throws<JsonException>(item.Arguments<ExportArguments>);
        }

        [Fact]
        public void State_reads_the_current_checkpoint_tolerantly_and_defaults_without_one()
        {
            JobItem<Job> item = JobTestItems.Create();
            Assert.Null(item.State<Checkpoint>());
            Assert.Equal(0, item.State<int>());

            item.StateJson = /*lang=json,strict*/ """{"lastId":42,"removed":"x"}""";
            Assert.Equal(42, item.State<Checkpoint>()!.LastId);
        }

        /// <summary>An argument type written with a required member, as a handler might.</summary>
        public sealed class ExportArguments
        {
            /// <summary>The export's format.</summary>
            public required string Format { get; init; }

            /// <summary>How many rows to export.</summary>
            public int Rows { get; init; }
        }

        /// <summary>An argument type with a primary constructor.</summary>
        /// <param name="Name">The report's name.</param>
        /// <param name="Count">How many copies.</param>
        public sealed record PositionalArguments(string Name, int Count);

        /// <summary>A handler's checkpoint.</summary>
        /// <param name="LastId">The last id processed.</param>
        public sealed record Checkpoint(int LastId);
    }
}
