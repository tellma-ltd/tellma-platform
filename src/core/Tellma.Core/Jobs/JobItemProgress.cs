// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §3.3: Report buffers one row for the next renewal, a value given replacing the
    // buffered one and a null keeping it, and makes its checkpoint the item's at once; Flush runs a
    // renewal now, carrying every buffered row of the tenant's in-flight jobs on this instance;
    // Append writes a fenced checkpoint inside the caller's transaction, whose checkpoint becomes
    // the item's only once that transaction committed, with job.changed riding the same commit.
    /// <summary>The progress of one claimed item, as its handler reports it.</summary>
    internal sealed class JobItemProgress : IJobProgress
    {
        /// <summary>The claim's lease.</summary>
        private readonly JobLease _lease;

        /// <summary>The item's slot, which holds its buffer.</summary>
        private readonly JobSlot _slot;

        /// <summary>Sets the item's checkpoint.</summary>
        private readonly Action<string> _setState;

        /// <summary>Runs the renewal a flush asks for.</summary>
        private readonly JobRenewal _renewal;

        /// <summary>The job scope's publisher, which an append's job.changed rides.</summary>
        private readonly IClientEventPublisher _publisher;

        /// <summary>Renders a progress message for the log.</summary>
        private readonly JobLogText _text;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the progress of one item.</summary>
        /// <param name="lease">The claim's lease.</param>
        /// <param name="slot">The item's slot.</param>
        /// <param name="setState">Sets the item's checkpoint.</param>
        /// <param name="renewal">Runs the renewal a flush asks for.</param>
        /// <param name="publisher">The job scope's publisher.</param>
        /// <param name="text">Renders a progress message for the log.</param>
        /// <param name="logger">The job machinery's logger.</param>
        public JobItemProgress(
            JobLease lease, JobSlot slot, Action<string> setState, JobRenewal renewal, IClientEventPublisher publisher, JobLogText text, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(lease);
            ArgumentNullException.ThrowIfNull(slot);
            ArgumentNullException.ThrowIfNull(setState);
            ArgumentNullException.ThrowIfNull(renewal);
            ArgumentNullException.ThrowIfNull(publisher);
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(logger);
            _lease = lease;
            _slot = slot;
            _setState = setState;
            _renewal = renewal;
            _publisher = publisher;
            _text = text;
            _logger = logger;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentOutOfRangeException">The percent exceeds 100.</exception>
        /// <exception cref="ArgumentException">The message's key or arguments do not fit the job table, or the checkpoint cannot be serialised.</exception>
        public void Report(byte? percent, JobMessage? message, object? state)
        {
            // --- Checked before anything is buffered ---
            (string? arguments, string? stateJson) = Prepare(percent, message, state);

            // --- Buffered for the next renewal; the checkpoint is the item's at once ---
            _slot.MarkObserved();
            _slot.Buffer(percent, message?.Key, arguments, stateJson);
            if (stateJson is not null)
            {
                _setState(stateJson);
            }

            Log(percent, message);
        }

        /// <inheritdoc />
        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            _slot.MarkObserved();
            return _renewal.RenewAsync(_lease.TenantId, cancellationToken);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentOutOfRangeException">The percent exceeds 100.</exception>
        /// <exception cref="ArgumentException">
        ///     The batch commits each statement on its own, so the checkpoint could not commit with the
        ///     caller's work; or the message or checkpoint does not fit the job table.
        /// </exception>
        public void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state)
        {
            ArgumentNullException.ThrowIfNull(batch);

            // --- A deferred batch records the append and replays it onto the batch it sends ---
            if (batch is RecordingBatch recording)
            {
                recording.Record(inner => Append(inner, percent, message, state));
                return;
            }

            DataBatch data = batch as DataBatch
                ?? throw new ArgumentException("A checkpoint is appended to a batch the platform created.", nameof(batch));
            if (data.TransactionMode == TransactionMode.None)
            {
                throw new ArgumentException(
                    $"The batch runs in {nameof(TransactionMode.None)} mode, where each statement commits on its own, so the checkpoint could not commit with the work it records. Append it to a batch in {nameof(TransactionMode.Auto)} or {nameof(TransactionMode.Explicit)} mode.",
                    nameof(batch));
            }

            (string? arguments, string? stateJson) = Prepare(percent, message, state);
            _slot.MarkObserved();

            // --- The fenced statement ---
            JobProgressList row = new()
            {
                Id = _slot.JobId,
                ProgressPercent = percent,
                ProgressKey = message?.Key,
                ProgressArgumentsJson = arguments,
                StateJson = stateJson,
            };
            JobStatements.AppendProgress(data, _lease.Token, [row]);

            // --- Once committed: the checkpoint becomes the item's unless a later one was reported,
            // and the buffer drops what the append wrote unless something was reported since ---
            long stateVersion = _slot.StateVersion;
            long bufferVersion = _slot.BufferVersion;
            data.OnCommitted(_ =>
            {
                if (stateJson is not null && _slot.StateVersion == stateVersion)
                {
                    _setState(stateJson);
                }

                _slot.Clear(bufferVersion, percent is not null, message is not null, stateJson is not null);
            });

            // --- The requester learns of the checkpoint with the commit ---
            if (_slot.RequestedById is int requester)
            {
                _publisher.Publish(data, JobNotices.Changed(_slot.JobId, requester));
            }

            Log(percent, message);
        }

        /// <summary>Checks and serialises what a report or an append writes.</summary>
        /// <param name="percent">The percent.</param>
        /// <param name="message">The message.</param>
        /// <param name="state">The checkpoint.</param>
        /// <returns>The message's arguments and the checkpoint as JSON.</returns>
        private static (string? Arguments, string? StateJson) Prepare(byte? percent, JobMessage? message, object? state)
        {
            if (percent > JobLimits.MaxProgressPercent)
            {
                throw new ArgumentOutOfRangeException(nameof(percent), percent, $"A job's progress is a percent from 0 to {JobLimits.MaxProgressPercent}.");
            }

            string? arguments = message is null ? null : JobJson.SerializeArguments(message, nameof(message));
            string? stateJson;
            try
            {
                stateJson = state is null ? null : JsonSerializer.Serialize(state, state.GetType(), TellmaJson.Options);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new ArgumentException($"The checkpoint of type {state!.GetType().Name} cannot be serialised as JSON: {exception.Message}", nameof(state), exception);
            }

            return (arguments, stateJson);
        }

        /// <summary>Logs the progress, its message rendered in English, when the log keeps debug events.</summary>
        /// <param name="percent">The percent.</param>
        /// <param name="message">The message.</param>
        private void Log(byte? percent, JobMessage? message)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                string? rendered = message is null ? null : _text.Render(_lease.Handler.HandlerType, message.Key, message.Arguments);
                JobsLog.JobProgress(_logger, _lease.Handler.Key, percent, rendered);
            }
        }
    }
}
