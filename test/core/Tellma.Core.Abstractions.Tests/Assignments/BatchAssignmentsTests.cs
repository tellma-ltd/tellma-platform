// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tests.Crud;

namespace Tellma.Core.Abstractions.Tests.Assignments
{
    /// <summary>A context's assignments write to the context's batch, for the context's stack.</summary>
    public class BatchAssignmentsTests
    {
        [Fact]
        public void An_assignment_targets_the_bound_stack_on_the_bound_batch()
        {
            (IAssignments service, RecordingProxy recorder) = RecordingProxy.For<IAssignments>();
            IDataBatch batch = CrudTestItems.UnusedBatch();
            BatchAssignments assignments = new(service, batch, "Widget");

            assignments.Assign(4, 9);

            RecordedCall call = recorder.Single(nameof(IAssignments.Assign));
            Assert.Same(batch, call.Arguments[0]);
            Assert.Equal([new AssignmentRequest("Widget", 4, 9)], (IEnumerable<AssignmentRequest>)call.Arguments[1]!);
        }

        [Fact]
        public void Completing_or_withdrawing_names_the_bound_stack_and_the_rows()
        {
            (IAssignments service, RecordingProxy recorder) = RecordingProxy.For<IAssignments>();
            BatchAssignments assignments = new(service, CrudTestItems.UnusedBatch(), "Widget");
            long[] completed = [4];
            long[] withdrawn = [5, 6];

            assignments.Complete(completed);
            assignments.Withdraw(withdrawn);

            Assert.Equal(
                [("Complete", "Widget", completed), ("Withdraw", "Widget", withdrawn)],
                recorder.Calls.Select(static call => (call.Method.Name, (string)call.Arguments[1]!, (long[])call.Arguments[2]!)));
            _ = Assert.Throws<ArgumentNullException>(() => assignments.Complete(null!));
        }

        [Fact]
        public void Bound_assignments_need_a_service_a_batch_and_a_stack()
        {
            (IAssignments service, _) = RecordingProxy.For<IAssignments>();

            _ = Assert.Throws<ArgumentNullException>(() => new BatchAssignments(null!, CrudTestItems.UnusedBatch(), "Widget"));
            _ = Assert.Throws<ArgumentNullException>(() => new BatchAssignments(service, null!, "Widget"));
            _ = Assert.Throws<ArgumentException>(() => new BatchAssignments(service, CrudTestItems.UnusedBatch(), " "));
        }

        [Fact]
        public void An_assignment_caption_and_request_default_to_nothing_extra()
        {
            AssignmentRequest request = new("Widget", 4, 9);

            Assert.Null(request.Comment);
            Assert.Null(request.Caption);
            Assert.Null(request.AssignedById);
            Assert.Equal(new AssignmentCaption(), new AssignmentCaption(null, null, null, null));
        }
    }
}
