// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Testing.Email
{
    /// <summary>
    ///     An <see cref="IEmailSender" /> that records every message instead of sending it, so a test
    ///     can assert on what a feature actually composed.
    /// </summary>
    /// <remarks>
    ///     Thread-safe, because the code under test may well send from several requests or background
    ///     workers at once. <see cref="WaitForAsync" /> is event-driven rather than polled, so a test
    ///     that triggers a send through a background worker neither sleeps nor flakes.
    /// </remarks>
    /// <param name="timeProvider">The clock capture timestamps come from; defaults to the system
    ///     clock. Supply a fake one when a test asserts on timing.</param>
    public sealed class CapturingEmailSender(TimeProvider? timeProvider = null) : IEmailSender
    {
        private readonly Lock _gate = new();
        private readonly List<CapturedEmail> _captured = [];
        private readonly List<PendingWait> _waits = [];
        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

        private Func<EmailMessage, int, EmailSendResult>? _script;
        private int _ordinal;

        /// <summary>Everything captured so far, oldest first.</summary>
        public IReadOnlyList<CapturedEmail> Captured
        {
            get
            {
                lock (_gate)
                {
                    return [.. _captured];
                }
            }
        }

        /// <summary>Scripts the result each message receives.</summary>
        /// <param name="script">Given the message and its zero-based ordinal <em>across this
        ///     sender's whole lifetime</em>, returns the result to report. The lifetime ordinal — not
        ///     the position within a batch — is what makes a retry scriptable: "fail the first
        ///     attempt, accept the second" is a statement about attempts, not about batches.</param>
        public void OnSending(Func<EmailMessage, int, EmailSendResult> script)
        {
            ArgumentNullException.ThrowIfNull(script);

            lock (_gate)
            {
                _script = script;
            }
        }

        /// <summary>Forgets everything captured so far.</summary>
        /// <remarks>
        ///     The script and the lifetime ordinal deliberately survive: clearing captures is about
        ///     narrowing what a later assertion sees, not about restarting the scenario.
        /// </remarks>
        public void Clear()
        {
            lock (_gate)
            {
                _captured.Clear();
            }
        }

        /// <summary>Waits until the captured messages satisfy a predicate.</summary>
        /// <param name="predicate">Evaluated against the whole capture list after every send.</param>
        /// <param name="timeout">How long to wait.</param>
        /// <returns>The captures that satisfied the predicate.</returns>
        /// <exception cref="TimeoutException">The predicate was not satisfied in time.</exception>
        public async Task<IReadOnlyList<CapturedEmail>> WaitForAsync(
            Func<IReadOnlyList<CapturedEmail>, bool> predicate, TimeSpan timeout)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            PendingWait wait;
            lock (_gate)
            {
                CapturedEmail[] snapshot = [.. _captured];
                if (predicate(snapshot))
                {
                    return snapshot;
                }

                // RunContinuationsAsynchronously so a completion never runs the awaiting test's
                // continuation while this lock is held.
                wait = new PendingWait(predicate);
                _waits.Add(wait);
            }

            return await wait.Completion.Task.WaitAsync(timeout).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);
            cancellationToken.ThrowIfCancellationRequested();

            List<EmailSendResult> results = new(messages.Count);
            List<PendingWait> satisfied = [];
            CapturedEmail[] snapshot;

            lock (_gate)
            {
                foreach (EmailMessage message in messages)
                {
                    int ordinal = _ordinal++;

                    // Bound by the same contract as any other transport: a structurally invalid
                    // message is rejected on its own rather than silently captured as sent.
                    EmailSendResult result = EmailMessageValidation.TryValidate(message, out string? error)
                        ? _script?.Invoke(message, ordinal)
                            ?? new EmailSendResult(EmailSendOutcome.Sent, $"capture-{ordinal}")
                        : new EmailSendResult(EmailSendOutcome.Rejected, Error: error);

                    results.Add(result);
                    _captured.Add(new CapturedEmail(message, result, _timeProvider.GetUtcNow(), ordinal));
                }

                snapshot = [.. _captured];
                for (int i = _waits.Count - 1; i >= 0; i--)
                {
                    if (_waits[i].Predicate(snapshot))
                    {
                        satisfied.Add(_waits[i]);
                        _waits.RemoveAt(i);
                    }
                }
            }

            foreach (PendingWait wait in satisfied)
            {
                wait.Completion.TrySetResult(snapshot);
            }

            return Task.FromResult<IReadOnlyList<EmailSendResult>>(results);
        }

        private sealed class PendingWait(Func<IReadOnlyList<CapturedEmail>, bool> predicate)
        {
            public Func<IReadOnlyList<CapturedEmail>, bool> Predicate { get; } = predicate;

            public TaskCompletionSource<IReadOnlyList<CapturedEmail>> Completion { get; } =
                new TaskCompletionSource<IReadOnlyList<CapturedEmail>>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
