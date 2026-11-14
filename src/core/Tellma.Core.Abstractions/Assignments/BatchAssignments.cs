// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Assignments
{
    /// <summary>
    ///     The assignments of a persist or action context: <see cref="IAssignments" /> bound to the
    ///     context's batch and its stack's resource, so a hook names only the rows and the users.
    /// </summary>
    /// <remarks>
    ///     A context hands one out only when the host registers <see cref="IAssignments" />; reading a
    ///     context's assignments otherwise throws <see cref="NotSupportedException" />.
    /// </remarks>
    public sealed class BatchAssignments
    {
        /// <summary>The service the calls forward to.</summary>
        private readonly IAssignments _assignments;

        /// <summary>The context's persist batch.</summary>
        private readonly IDataBatch _batch;

        /// <summary>The context's stack's entity name.</summary>
        private readonly string _resource;

        /// <summary>Binds the service to a context's batch and resource.</summary>
        /// <param name="assignments">The scope's service.</param>
        /// <param name="batch">The context's persist batch.</param>
        /// <param name="resource">The context's stack's entity name.</param>
        /// <exception cref="ArgumentNullException"><paramref name="assignments" /> or <paramref name="batch" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="resource" /> is null, empty or white space.</exception>
        internal BatchAssignments(IAssignments assignments, IDataBatch batch, string resource)
        {
            ArgumentNullException.ThrowIfNull(assignments);
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            _assignments = assignments;
            _batch = batch;
            _resource = resource;
        }

        /// <summary>Appends one assignment of a row of the context's stack.</summary>
        /// <param name="targetId">The row's id; positive.</param>
        /// <param name="assigneeId">The user the row is assigned to; positive.</param>
        /// <param name="comment">The assigner's comment; null for none.</param>
        /// <param name="caption">How the assignment reads in the assignee's list; null for the default.</param>
        /// <exception cref="InvalidOperationException">The assignment breaks a rule.</exception>
        public void Assign(long targetId, int assigneeId, string? comment = null, AssignmentCaption? caption = null)
        {
            _assignments.Assign(_batch, [new AssignmentRequest(_resource, targetId, assigneeId, comment, caption)]);
        }

        /// <summary>Appends the completion of the open assignments of rows of the context's stack.</summary>
        /// <param name="targetIds">The rows' ids.</param>
        /// <exception cref="ArgumentNullException"><paramref name="targetIds" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The call breaks a rule.</exception>
        public void Complete(IReadOnlyList<long> targetIds)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            _assignments.Complete(_batch, _resource, targetIds);
        }

        /// <summary>Appends the withdrawal of the open assignments of rows of the context's stack.</summary>
        /// <param name="targetIds">The rows' ids.</param>
        /// <exception cref="ArgumentNullException"><paramref name="targetIds" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The call breaks a rule.</exception>
        public void Withdraw(IReadOnlyList<long> targetIds)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            _assignments.Withdraw(_batch, _resource, targetIds);
        }

        /// <summary>
        ///     Binds a context's assignments, or refuses when the host registers no service: the
        ///     shared rule of every context that exposes assignments.
        /// </summary>
        /// <param name="assignments">The scope's service; null while the host registers none.</param>
        /// <param name="batch">The context's persist batch.</param>
        /// <param name="resource">The context's stack's entity name; null when no pipeline prepared the context.</param>
        /// <returns>The bound assignments.</returns>
        /// <exception cref="NotSupportedException"><paramref name="assignments" /> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="resource" /> is null.</exception>
        internal static BatchAssignments For(IAssignments? assignments, IDataBatch batch, string? resource)
        {
            // An absent service is the host's choice and is refused as unsupported; an absent
            // resource means the context never came from a pipeline.
            return assignments is null
                ? throw new NotSupportedException(
                    $"Assignments are not available: the host registers no {nameof(IAssignments)} service, so rows cannot be assigned, completed or withdrawn.")
                : resource is null
                    ? throw new InvalidOperationException("The context was not prepared by the pipeline, so it names no resource to assign rows of.")
                    : new BatchAssignments(assignments, batch, resource);
        }
    }
}
