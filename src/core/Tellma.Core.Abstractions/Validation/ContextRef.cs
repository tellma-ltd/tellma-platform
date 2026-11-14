// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     A value a validator asked the context loader for, filled when the round that carries the
    ///     request is dispatched. A load method returns it at once; the validator reads
    ///     <see cref="Value" /> after awaiting the loader's <see cref="IContextLoader.LoadAsync" />.
    /// </summary>
    /// <remarks>A handle compared by reference: two refs are two requests, even when the loader merged them into one statement.</remarks>
    /// <typeparam name="T">The value's type.</typeparam>
    public sealed class ContextRef<T>
    {
        /// <summary>Creates a ref its round fills when it is dispatched.</summary>
        internal ContextRef()
        {
            // Unread until IsLoaded: the getter throws first.
            Value = default!;
        }

        /// <summary>Creates a ref that is already loaded, such as one answered from a primed row.</summary>
        /// <param name="value">The value.</param>
        public ContextRef(T value)
        {
            Value = value;
            IsLoaded = true;
        }

        /// <summary>Whether the value is available: the round carrying the request ran.</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>The value.</summary>
        /// <exception cref="InvalidOperationException">The round carrying the request has not run: await the loader's load first.</exception>
        public T Value
        {
            get => IsLoaded
                ? field
                : throw new InvalidOperationException(
                    "The context value is not loaded yet. Await the context loader's LoadAsync before reading a value it was asked for.");
            private set;
        }

        /// <summary>Fills the ref with the round's value, once.</summary>
        /// <param name="value">The value.</param>
        /// <exception cref="InvalidOperationException">The ref is already loaded.</exception>
        internal void Complete(T value)
        {
            if (IsLoaded)
            {
                throw new InvalidOperationException("A context value is loaded once.");
            }

            Value = value;
            IsLoaded = true;
        }
    }
}
