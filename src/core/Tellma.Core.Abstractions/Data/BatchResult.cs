// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The result of one statement of a batch, available once the batch has executed and the
    ///     statement's result sets were read.
    /// </summary>
    /// <typeparam name="T">The result's type.</typeparam>
    /// <remarks>
    ///     A handle compared by reference: two pending results are two statements, never one. A
    ///     statement whose body did not run (the connect prologue's guard failed, or an earlier
    ///     statement threw) leaves its result incomplete.
    /// </remarks>
    public sealed class BatchResult<T>
    {
        /// <summary>Creates a result its batch completes when it executes.</summary>
        internal BatchResult()
        {
            // Unread until IsCompleted: the getter throws first.
            Value = default!;
        }

        /// <summary>Creates a result that is already complete, such as a cache hit.</summary>
        /// <param name="value">The value.</param>
        public BatchResult(T value)
        {
            Value = value;
            IsCompleted = true;
        }

        /// <summary>The value.</summary>
        /// <exception cref="InvalidOperationException">The statement has not been read yet.</exception>
        public T Value
        {
            get => IsCompleted
                ? field
                : throw new InvalidOperationException(
                    "The batch statement has no result yet. Read a batch result after the batch executed, and only when the statement ran.");
            private set;
        }

        /// <summary>Whether the value is available.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>Completes the result with the statement's value, once.</summary>
        /// <param name="value">The value.</param>
        /// <exception cref="InvalidOperationException">The result is already complete.</exception>
        internal void Complete(T value)
        {
            if (IsCompleted)
            {
                throw new InvalidOperationException("A batch result is completed once.");
            }

            Value = value;
            IsCompleted = true;
        }
    }
}
