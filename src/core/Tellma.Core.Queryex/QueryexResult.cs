// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     The outcome of a compilation: a value or diagnostics, never an exception.
    /// </summary>
    /// <typeparam name="T">The compiled value's type.</typeparam>
    public sealed class QueryexResult<T>
        where T : class
    {
        /// <summary>Creates a successful result.</summary>
        /// <param name="value">The compiled value.</param>
        internal QueryexResult(T value)
        {
            Value = value;
            Diagnostics = [];
        }

        /// <summary>Creates a failed result.</summary>
        /// <param name="diagnostics">The diagnostics, which must not be empty.</param>
        internal QueryexResult(IReadOnlyList<QueryexDiagnostic> diagnostics)
        {
            Value = null;
            Diagnostics = diagnostics;
        }

        /// <summary>
        ///     True when compilation produced a value, in which case <see cref="Diagnostics" /> is
        ///     empty.
        /// </summary>
        [MemberNotNullWhen(true, nameof(Value))]
        public bool Succeeded => Value is not null;

        /// <summary>The compiled value, when <see cref="Succeeded" />.</summary>
        public T? Value { get; }

        /// <summary>
        ///     The diagnostics, when not <see cref="Succeeded" />. As complete as the input allows:
        ///     independent problems are all reported, not just the first.
        /// </summary>
        public IReadOnlyList<QueryexDiagnostic> Diagnostics { get; }
    }
}
