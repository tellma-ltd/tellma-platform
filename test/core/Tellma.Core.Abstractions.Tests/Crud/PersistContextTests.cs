// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tests.Access;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Tests.Crud
{
    /// <summary>
    ///     The contexts a persist-stage component and a details contributor receive, and the
    ///     enlistment entry points: what each offers before and after the pipeline prepares it.
    /// </summary>
    public class PersistContextTests
    {
        /// <summary>A notification request.</summary>
        private static readonly NotificationRequest Request = new("widget.archived", [1], null, "Widget", 4, null, null);

        [Fact]
        public void Notifications_are_refused_as_unsupported_while_the_host_registers_no_notifier()
        {
            SavePersistContext<Widget> context = CrudTestItems.SavePersistContext([]);

            NotSupportedException refused = Assert.Throws<NotSupportedException>(() => context.Notify(Request));
            Assert.Contains(nameof(INotifier), refused.Message, StringComparison.Ordinal);
            _ = Assert.Throws<NotSupportedException>(() => context.Withdraw(new WithdrawRequest("widget:4")));
            _ = Assert.Throws<ArgumentNullException>(() => context.Notify(null!));
        }

        [Fact]
        public void Notifications_join_the_context_batch()
        {
            (INotifier notifier, RecordingProxy recorder) = RecordingProxy.For<INotifier>();
            IDataBatch batch = CrudTestItems.UnusedBatch();
            SavePersistContext<Widget> context = With(CrudTestItems.SavePersistContext([]), batch, notifier: notifier);
            WithdrawRequest withdraw = new("widget:4");

            context.Notify(Request);
            context.Withdraw(withdraw);

            Assert.Equal(["Notify", "Withdraw"], recorder.Calls.Select(static call => call.Method.Name));
            Assert.All(recorder.Calls, call => Assert.Same(batch, call.Arguments[0]));
            Assert.Equal([Request], (IEnumerable<NotificationRequest>)recorder.Calls[0].Arguments[1]!);
            Assert.Equal([withdraw], (IEnumerable<WithdrawRequest>)recorder.Calls[1].Arguments[1]!);
        }

        [Fact]
        public void Assignments_are_refused_as_unsupported_while_the_host_registers_no_service()
        {
            SavePersistContext<Widget> context = CrudTestItems.SavePersistContext([]);

            NotSupportedException refused = Assert.Throws<NotSupportedException>(() => context.Assignments);
            Assert.Contains(nameof(IAssignments), refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Assignments_bind_the_context_batch_and_its_stack()
        {
            (IAssignments service, RecordingProxy recorder) = RecordingProxy.For<IAssignments>();
            IDataBatch batch = CrudTestItems.UnusedBatch();
            SavePersistContext<Widget> context = With(CrudTestItems.SavePersistContext([]), batch, assignments: service, resource: "Widget");
            AssignmentCaption caption = new(Code: "W-4");

            context.Assignments.Assign(4, 9, "please review", caption);
            context.Assignments.Complete([4]);
            context.Assignments.Withdraw([5]);

            Assert.Equal(["Assign", "Complete", "Withdraw"], recorder.Calls.Select(static call => call.Method.Name));
            Assert.All(recorder.Calls, call => Assert.Same(batch, call.Arguments[0]));
            Assert.Equal(
                [new AssignmentRequest("Widget", 4, 9, "please review", caption)],
                (IEnumerable<AssignmentRequest>)recorder.Calls[0].Arguments[1]!);
            Assert.Equal(("Widget", "Widget"), ((string)recorder.Calls[1].Arguments[1]!, (string)recorder.Calls[2].Arguments[1]!));
        }

        [Fact]
        public void Assignments_on_a_context_no_pipeline_prepared_name_no_stack()
        {
            (IAssignments service, _) = RecordingProxy.For<IAssignments>();
            SavePersistContext<Widget> context = With(CrudTestItems.SavePersistContext([]), CrudTestItems.UnusedBatch(), assignments: service);

            _ = Assert.Throws<InvalidOperationException>(() => context.Assignments);
        }

        [Fact]
        public void A_before_image_is_read_by_row_index_once_the_pipeline_has_loaded_them()
        {
            Widget updated = new() { Name = "new" };
            Widget before = new() { Name = "old" };
            SavePersistContext<Widget> unprepared = CrudTestItems.SavePersistContext([updated]);
            SavePersistContext<Widget> prepared = new()
            {
                Entities = [new Widget(), updated],
                Context = CrudTestItems.Context(),
                Batch = CrudTestItems.UnusedBatch(),
                AffectedIds = new SqlIdentifier("@affected"),
                Captures = [],
                Options = new SaveOptions(),
                NewIds = new SqlIdentifier("@new"),
                TouchedIds = new SqlIdentifier("@touched"),
                BeforeImages = [null, before],
            };

            _ = Assert.Throws<InvalidOperationException>(() => unprepared.Before(0));
            Assert.Null(prepared.Before(0));
            Assert.Same(before, prepared.Before(1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => prepared.Before(2));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => prepared.Before(-1));
        }

        [Fact]
        public void A_persist_context_refuses_missing_members()
        {
            _ = Assert.Throws<ArgumentNullException>(() => CrudTestItems.SavePersistContext(null!));
            _ = Assert.Throws<ArgumentException>(() => new ActionPersistContext<Widget>
            {
                Entities = [],
                Context = CrudTestItems.Context(),
                Batch = CrudTestItems.UnusedBatch(),
                AffectedIds = new SqlIdentifier("@affected"),
                Captures = [],
                Action = " ",
            });
        }

        [Fact]
        public void A_details_plan_no_planner_prepared_takes_no_extras()
        {
            DetailsPlan<Widget, int> plan = Plan(sink: null);

            _ = Assert.Throws<InvalidOperationException>(() => plan.AddExtra("total", $"SELECT 1", null));
            _ = Assert.Throws<InvalidOperationException>(() => plan.AddComputedExtra("count", static rows => rows.Count));
        }

        [Fact]
        public void A_details_plan_hands_each_extra_and_read_to_its_planner()
        {
            Sink sink = new();
            DetailsPlan<Widget, int> plan = Plan(sink);
            QuerySpec spec = new() { Root = AccessFixture.Schema.FindEntity(AccessFixture.Widget)!, Select = "Id" };
            FormattableString sql = $"SELECT 1";
            Func<IReadOnlyList<Widget>, int> compute = static rows => rows.Count;

            plan.AddExtra("total", sql, null);
            plan.AddExtra("lines", spec, null);
            _ = plan.AddRead(spec, null);
            _ = plan.AddRead(sql, null);
            plan.AddComputedExtra("count", compute);

            Assert.Equal(["extra:total:sql", "extra:lines:query", "read:query", "read:sql", "computed:count"], sink.Calls);
            Assert.Same(compute, sink.Compute);
            _ = Assert.Throws<ArgumentException>(() => plan.AddExtra(" ", sql, null));
            _ = Assert.Throws<ArgumentNullException>(() => plan.AddRead((QuerySpec)null!, null));
        }

        [Fact]
        public async Task A_write_enlists_only_with_a_frame_the_platform_opened()
        {
            Writer writer = new();

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => writer.EnlistSaveAsync(CrudTestItems.ForeignHost(), [new Widget()]));
            Assert.Contains("test.operation", refused.Message, StringComparison.Ordinal);
            _ = Assert.Throws<InvalidOperationException>(() => writer.EnlistDelete(CrudTestItems.ForeignHost(), [1]));
            _ = Assert.Throws<ArgumentNullException>(() => writer.EnlistDelete(null!, [1]));
        }

        [Fact]
        public async Task A_write_enlists_with_the_frame_under_default_options_when_none_are_given()
        {
            Writer writer = new();
            Frame frame = new();
            Widget[] rows = [new()];
            EnlistDeleteOptions options = new() { Order = EnlistOrder.AfterHost };

            EnlistedSave<Widget> saved = await writer.EnlistSaveAsync(frame, rows);
            EnlistedDelete<int> deleted = writer.EnlistDelete(frame, [3], options);

            Assert.Same(rows, saved.Rows);
            Assert.Equal("Widget", saved.Resource);
            Assert.Equal([3], deleted.Ids);
            Assert.Equal(EnlistOrder.AfterHost, frame.SaveOptions!.Order);
            Assert.Same(options, frame.DeleteOptions);
            Assert.Same(writer, frame.Writer);
        }

        /// <summary>A copy of a save-persist context with the runtime's members set.</summary>
        /// <param name="context">The context to copy.</param>
        /// <param name="batch">The batch.</param>
        /// <param name="notifier">The notifier; null for none.</param>
        /// <param name="assignments">The assignments service; null for none.</param>
        /// <param name="resource">The stack's entity name; null for none.</param>
        /// <returns>The copy.</returns>
        private static SavePersistContext<Widget> With(
            SavePersistContext<Widget> context, IDataBatch batch, INotifier? notifier = null, IAssignments? assignments = null, string? resource = null)
        {
            return new SavePersistContext<Widget>
            {
                Entities = context.Entities,
                Context = context.Context,
                Batch = batch,
                AffectedIds = context.AffectedIds,
                Captures = context.Captures,
                Options = context.Options,
                NewIds = context.NewIds,
                TouchedIds = context.TouchedIds,
                Notifier = notifier,
                AssignmentsService = assignments,
                Resource = resource,
            };
        }

        /// <summary>A details plan over one id.</summary>
        /// <param name="sink">The planner; null for a plan no planner prepared.</param>
        /// <returns>The plan.</returns>
        private static DetailsPlan<Widget, int> Plan(Sink? sink)
        {
            return new DetailsPlan<Widget, int>
            {
                Request = DetailsRequest.None,
                Context = CrudTestItems.Context(),
                IdsSource = new SqlIdentifier("@ids"),
                Ids = [1],
                Sink = sink,
            };
        }

        /// <summary>A planner that records what a plan handed it.</summary>
        private sealed class Sink : IDetailsPlanSink<Widget>
        {
            /// <summary>The calls, each as kind, name and source.</summary>
            public List<string> Calls { get; } = [];

            /// <summary>The computation handed with the last computed extra.</summary>
            public object? Compute { get; private set; }

            /// <inheritdoc />
            public void AddExtra(string name, FormattableString sql, SqlOptions? options)
            {
                Calls.Add("extra:" + name + ":sql");
            }

            /// <inheritdoc />
            public void AddExtra(string name, QuerySpec spec, QueryArguments? arguments)
            {
                Calls.Add("extra:" + name + ":query");
            }

            /// <inheritdoc />
            public BatchResult<RowPage> AddRead(QuerySpec spec, QueryArguments? arguments)
            {
                Calls.Add("read:query");
                return null!;
            }

            /// <inheritdoc />
            public BatchResult<RawResult> AddRead(FormattableString sql, SqlOptions? options)
            {
                Calls.Add("read:sql");
                return null!;
            }

            /// <inheritdoc />
            public void AddComputedExtra<TValue>(string name, Func<IReadOnlyList<Widget>, TValue> compute)
            {
                Calls.Add("computed:" + name);
                Compute = compute;
            }
        }

        /// <summary>A stack that enlists writes to widgets.</summary>
        private sealed class Writer : IEnlists<Widget>;

        /// <summary>A platform frame that accepts every enlistment and remembers it.</summary>
        private sealed class Frame : IWriteHost, IEnlistmentHost
        {
            /// <inheritdoc />
            public WriteHostKind Kind => WriteHostKind.Pipeline;

            /// <inheritdoc />
            public string Operation => "save";

            /// <inheritdoc />
            public IConfirmations Confirmations => throw new NotSupportedException("Not used by these tests.");

            /// <summary>The writer of the last enlistment.</summary>
            public object? Writer { get; private set; }

            /// <summary>The options of the last enlisted save.</summary>
            public EnlistSaveOptions? SaveOptions { get; private set; }

            /// <summary>The options of the last enlisted delete.</summary>
            public EnlistDeleteOptions? DeleteOptions { get; private set; }

            /// <inheritdoc />
            public Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(IEnlists<TTarget> writer, IReadOnlyList<TTarget> rows, EnlistSaveOptions options)
            {
                Writer = writer;
                SaveOptions = options;
                return Task.FromResult(new EnlistedSave<TTarget>(rows, typeof(TTarget).Name));
            }

            /// <inheritdoc />
            public EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(IEnlists<TTarget> writer, IReadOnlyList<TKey> ids, EnlistDeleteOptions options)
                where TTarget : Entity<TKey>
                where TKey : struct, IBinaryInteger<TKey>
            {
                Writer = writer;
                DeleteOptions = options;
                return new EnlistedDelete<TKey>(ids, typeof(TTarget).Name);
            }
        }
    }
}
