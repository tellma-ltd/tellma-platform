// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3, an open frame's PersistAsync: the work leaves the rounds, which end once every
    // group's tasks completed (the rounds pending ride Validate batches); the groups' errors, then
    // their unconfirmed warnings, refuse the persist; then one Persist batch carries every group,
    // the BeforeHost groups first, each with its own groups around it, under the access guards when
    // any group writes a security table outside the system scope, and with no post-check: the
    // targets' grants are never asked. A failure the executor attributes to a group's statement is
    // translated against that group and re-homed. Once committed, each group's post-commit phase runs
    // in the order the batch composed them. A job's frame appends its groups to the job's completion
    // batch instead, which the worker sends.
    /// <summary>The frame of an API action, a job's handler or a provisioning step: its persist.</summary>
    internal sealed partial class OpenWriteFrame
    {
        /// <summary>The resource a provisioning step's frame names.</summary>
        private const string ProvisioningResource = "provisioning";

        /// <summary>The resource a job's frame names.</summary>
        private const string JobResource = "job";

        /// <summary>The request client of an agent's call, whose writes are the agent's.</summary>
        private const string AgentClient = "mcp";

        /// <summary>The dependency a data-access failure names.</summary>
        private const string DatabaseDependency = "database";

        /// <summary>The wait a caller is told before reloading after a commit the database could not confirm.</summary>
        private static readonly TimeSpan DatabaseRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>The times the runner entered the persist's compose.</summary>
        private int _entries;

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">
        ///     The frame persisted already; or the call comes from outside the frame's own work, or while
        ///     an operation the work runs through the front door is still validating.
        /// </exception>
        public async Task<EnlistmentOutcome> PersistAsync(CancellationToken cancellationToken = default)
        {
            // --- From the work itself, never while an operation nested in the frame composes, and once ---
            if (_rounds is null || _work is null || !ReferenceEquals(_rounds.Scheduler.CurrentTask, _work))
            {
                throw new InvalidOperationException(
                    $"PersistAsync of {Operation} was called from outside the frame's own method, handler or step. Persist the frame from the work it was opened for, never from a validator or another flow.");
            }

            if (Volatile.Read(ref _nested) > 0)
            {
                throw new InvalidOperationException(
                    $"PersistAsync of {Operation} was called while an operation the frame runs through a service is still validating. Await that operation before persisting the frame.");
            }

            if (Interlocked.Exchange(ref _persists, 1) == 1)
            {
                throw new InvalidOperationException($"The frame of {Operation} persists once, and PersistAsync was called on it again.");
            }

            // --- The work leaves the rounds: they end once every group's tasks completed ---
            _persisting = true;
            _rounds.Scheduler.BeginLeave()();
            _ = _left.TrySetResult();
            await using CancellationTokenRegistration linked = cancellationToken.Register(
                static state => ((CancellationTokenSource)state!).Cancel(),
                _cancellation);
            ExceptionDispatchInfo? failed = await _ended.Task.ConfigureAwait(false);
            failed?.Throw();

            // --- What follows the rounds, then the outcome in the order a caller can act on ---
            _rounds.Close(complete: true);
            ValidationOutcome.Raise(staleStamp: null, _errors, _confirmations!);

            // --- Nothing enlisted: nothing to send, and no stamp but the frame's clock; else the
            // groups, on the job's batch or one of their own ---
            return _rounds.Groups.Count == 0
                ? new EnlistmentOutcome(_services.Time.GetUtcNow(), _run.Tags(committed: null), UserVersionTags: null)
                : _completion is IDataBatch completion
                    ? await AppendAsync(completion).ConfigureAwait(false)
                    : await SendAsync().ConfigureAwait(false);
        }

        /// <summary>Runs the post-commit phase of a job frame's groups, once the worker committed the completion batch.</summary>
        /// <param name="committed">The outcome of the completion batch; null when none answered.</param>
        /// <returns>A task that completes when every group's participants returned.</returns>
        /// <exception cref="InvalidOperationException">The frame is not a job's, or it appended nothing.</exception>
        public Task AfterCompletionAsync(BatchOutcome? committed)
        {
            if (_completion is null || Volatile.Read(ref _entries) == 0)
            {
                throw new InvalidOperationException($"The frame of {Operation} appended nothing to a completion batch, so no commit of one is its to report.");
            }

            _services.Accessor.Current.ApplyCultures();
            return AfterGroupsCommitAsync(committed);
        }

        /// <summary>Sends every group in one persist batch, then runs their post-commit phase.</summary>
        /// <returns>The batch's stamp and the version tags after its bumps.</returns>
        /// <exception cref="Abstractions.Tenancy.TenantUnavailableException">The tenant's state admits no persist.</exception>
        private async Task<EnlistmentOutcome> SendAsync()
        {
            // --- The tenant-state verdict, judged by the frame itself ---
            PersistVerdict.Ensure(_services.Accessor.Current, mutation: true, allowWhenReadOnly: false, _services.Frames.Open);

            // --- One Persist batch ---
            BatchOutcome? outcome;
            try
            {
                outcome = await _services.Runner.RunAsync(BatchPurpose.Persist, ComposeAsync, static (_, outcome) => outcome, _cancellation.Token).ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // The probe found an inserted row: the batch committed, its response lost with the connection.
                outcome = null;
            }
            catch (DataAccessAmbiguousException ambiguous)
            {
                // A batch that cannot be probed: the caller reloads to learn whether it committed.
                throw new DependencyUnavailableException(DatabaseDependency, DatabaseRetryAfter, ambiguous);
            }
            catch (DataAccessException failure)
            {
                _services.Accessor.Current.ApplyCultures();
                if (Rounds.Translate(failure) is Exception translated)
                {
                    throw translated;
                }

                throw;
            }

            // --- The commit: each group's post-commit phase, in the order the batch composed them ---
            _services.Accessor.Current.ApplyCultures();
            await AfterGroupsCommitAsync(outcome).ConfigureAwait(false);

            // --- The stamp a save statement of the batch reported. A batch of deletes alone stamped no
            // row, and a commit whose response was lost reported none: the frame's clock stands in,
            // as the outcome's contract says, never mistaken for a stored stamp ---
            DateTimeOffset stamp = Rounds.Groups.Select(static group => group.Stamp).FirstOrDefault(static stamp => stamp is not null) ?? _services.Time.GetUtcNow();
            return new EnlistmentOutcome(stamp, _run.Tags(outcome), outcome?.UserVersionTags);
        }

        // Spec 0015 §13.3 and spec 0020 §3.1: the job's completion batch carries the outcomes first,
        // then the groups, and commits only while the lease holds. The batch's stamp is not known
        // until the worker sent it, so the outcome is the frame's own clock and the tags it last saw.
        /// <summary>Appends every group to a job's completion batch.</summary>
        /// <param name="completion">The completion batch.</param>
        /// <returns>The provisional outcome: the frame's clock, and the tenant's last-seen tags.</returns>
        private async Task<EnlistmentOutcome> AppendAsync(IDataBatch completion)
        {
            // The worker composes the batch for the run-as user; the groups ask no grant of their own.
            await ComposeOntoAsync(user: null, completion).ConfigureAwait(false);
            return new EnlistmentOutcome(_services.Time.GetUtcNow(), _run.Tags(committed: null), UserVersionTags: null);
        }

        /// <summary>Composes the persist batch: every group, on the first entry live and on a re-entry recomposed and replayed.</summary>
        /// <param name="user">The connected user the batch is composed for.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>A task that completes when the batch is composed.</returns>
        private ValueTask ComposeAsync(ConnectedUser user, IDataBatch batch)
        {
            return ComposeOntoAsync(user, batch);
        }

        /// <summary>Composes every group onto a batch, then the access guards when the batch writes a security table.</summary>
        /// <param name="user">The connected user the batch is composed for; null when the batch names none.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>A task that completes when the batch is composed.</returns>
        /// <exception cref="StaleContextException">On a re-entry, a tag the rounds were served under moved.</exception>
        private async ValueTask ComposeOntoAsync(ConnectedUser? user, IDataBatch batch)
        {
            // --- A re-entry recomposes what the rounds validated, unless what they read moved ---
            bool first = Interlocked.Increment(ref _entries) == 1;
            _run.Prepare(user, batch, first);
            IReadOnlySet<TableName> callers = batch.CallerAuthority;

            // --- The groups: those ahead of the host, then those after it, each with its own nested ---
            await ComposeGroupsAsync(batch, EnlistOrder.BeforeHost, first).ConfigureAwait(false);
            await ComposeGroupsAsync(batch, EnlistOrder.AfterHost, first).ConfigureAwait(false);

            // --- The access guards, after every group, under the caller's authority again, when the
            // batch writes a security table: a group's at any depth, or any statement's declared writes ---
            FrameRun.AuthorizeFor(batch, callers);
            _ = PersistGuards.Contribute(_services.AccessGuards, _services.Accessor.Current, batch, Rounds.GroupTables);
        }

        /// <summary>The write steps an open frame shares with the pipeline: its rounds' dispatch, its reads' decisions and its persist's preparation.</summary>
        /// <param name="operation">The scope's write services.</param>
        /// <param name="resource">The resource the frame's logs name.</param>
        /// <param name="operationName">The <c>crud.operation</c> the frame runs as.</param>
        private sealed class FrameRun(SaveOperation operation, string resource, string operationName) : WriteRun(operation, resource, operationName)
        {
            /// <summary>Sets the authority of the appender the batch is composed for next.</summary>
            /// <param name="batch">The batch.</param>
            /// <param name="tables">The stack-owned tables the appender may write.</param>
            public static void AuthorizeFor(IDataBatch batch, IReadOnlySet<TableName> tables)
            {
                Authorize(batch, tables);
            }

            /// <summary>The caller's read decision on an entity, for the user the current round is composed for.</summary>
            /// <param name="entityType">The entity's class.</param>
            /// <returns>The decision.</returns>
            public AccessDecision ReadDecisionOf(Type entityType)
            {
                return ReadDecision(entityType);
            }

            /// <summary>Dispatches the loads of one round on one guarded batch.</summary>
            /// <param name="loader">The frame's loader.</param>
            /// <param name="cancellationToken">Cancels the round.</param>
            /// <returns>A task that completes when the round's refs are filled.</returns>
            public Task DispatchAsync(ContextLoader loader, CancellationToken cancellationToken)
            {
                return DispatchRoundAsync(loader, cancellationToken);
            }

            /// <summary>Prepares a persist batch: on a re-entry, refuses what the rounds read as stale; then declares the tags they were served under.</summary>
            /// <param name="user">The connected user the batch is composed for; null when the batch names none.</param>
            /// <param name="batch">The batch.</param>
            /// <param name="first">Whether this is the batch's first composition.</param>
            /// <exception cref="StaleContextException">On a re-entry, a tag the rounds were served under moved.</exception>
            public void Prepare(ConnectedUser? user, IDataBatch batch, bool first)
            {
                if (user is not null)
                {
                    if (!first)
                    {
                        EnsureServedTagsHold(user);
                    }

                    Composer = user;
                }

                batch.Limits = QueryexProfiles.PipelineLimits;
                DependOnServed(batch);
            }

            /// <summary>The tenant's version tags after a commit.</summary>
            /// <param name="committed">The outcome of the batch that committed; null when none answered.</param>
            /// <returns>The tags.</returns>
            public VersionTagSnapshot Tags(BatchOutcome? committed)
            {
                return TagsAfter(committed);
            }
        }
    }
}
