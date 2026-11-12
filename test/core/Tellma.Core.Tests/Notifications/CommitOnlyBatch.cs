// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     A fake <see cref="IDataBatch" /> that supports commit callbacks and its composing state
    ///     alone: it records the callbacks, refuses one registered after it executed, as a real batch
    ///     does, and runs them when the test commits it. Every other member throws.
    /// </summary>
    /// <remarks>A dispatch proxy, so the fake does not restate the batch's many members; create it with <see cref="Create" />.</remarks>
    public class CommitOnlyBatch : DispatchProxy
    {
        private readonly List<Action<BatchOutcome>> _callbacks = [];
        private bool _executed;

        /// <summary>The callbacks registered so far, in registration order.</summary>
        public IReadOnlyList<Action<BatchOutcome>> Callbacks => _callbacks;

        /// <summary>Creates a batch and its fake.</summary>
        /// <returns>The batch, and the fake behind it.</returns>
        public static (IDataBatch Batch, CommitOnlyBatch Fake) Create()
        {
            IDataBatch batch = Create<IDataBatch, CommitOnlyBatch>();
            return (batch, (CommitOnlyBatch)(object)batch);
        }

        /// <summary>Executes the batch as committed: runs every callback in registration order.</summary>
        public void Commit()
        {
            _executed = true;
            BatchOutcome outcome = new(new VersionTagSnapshot(1, new Dictionary<string, VersionTag>(), DateTimeOffset.UnixEpoch), null, 1);
            foreach (Action<BatchOutcome> callback in _callbacks)
            {
                callback(outcome);
            }
        }

        /// <summary>Executes the batch as failed: runs no callback.</summary>
        public void Fail()
        {
            _executed = true;
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            // The composing state, read through its getter.
            if (targetMethod?.Name == "get_" + nameof(IDataBatch.IsComposing))
            {
                return !_executed;
            }

            if (targetMethod?.Name != nameof(IDataBatch.OnCommitted))
            {
                throw new NotSupportedException($"The fake batch supports {nameof(IDataBatch.IsComposing)} and {nameof(IDataBatch.OnCommitted)} alone, not {targetMethod?.Name}.");
            }

            // A real batch refuses a callback once it executed.
            if (_executed)
            {
                throw new InvalidOperationException("The batch already executed.");
            }

            _callbacks.Add((Action<BatchOutcome>)args![0]!);
            return null;
        }
    }
}
