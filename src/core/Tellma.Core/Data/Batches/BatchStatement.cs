// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>Reads one statement's result sets off the round trip's cursor, in order, and completes its handles.</summary>
    /// <param name="cursor">The round trip's cursor, positioned before the statement's first set.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the statement's sets are read.</returns>
    internal delegate ValueTask StatementReader(ResultSetCursor cursor, CancellationToken cancellationToken);

    /// <summary>One statement of a batch: its text, its parameters and what it declares about itself.</summary>
    internal sealed class BatchStatement
    {
        /// <summary>Creates the statement.</summary>
        /// <param name="ordinal">The batch ordinal its batch-local names carry.</param>
        /// <param name="section">The frame section it is emitted in.</param>
        /// <param name="text">The text; empty for a statement that only binds parameters.</param>
        /// <param name="parameters">The parameters it binds.</param>
        /// <param name="writes">The tables it writes.</param>
        /// <param name="idempotent">Whether running it twice has the effect of running it once.</param>
        /// <param name="reader">Reads its result sets, or null when it returns none.</param>
        public BatchStatement(
            int ordinal,
            BatchSection section,
            string text,
            IReadOnlyList<BatchParameter> parameters,
            IReadOnlySet<TableName> writes,
            bool idempotent,
            StatementReader? reader)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(parameters);
            ArgumentNullException.ThrowIfNull(writes);

            Ordinal = ordinal;
            Section = section;
            Text = text;
            Parameters = parameters;
            Writes = writes;
            Idempotent = idempotent;
            Reader = reader;
        }

        /// <summary>The batch ordinal its batch-local names carry.</summary>
        public int Ordinal { get; }

        /// <summary>The frame section it is emitted in.</summary>
        public BatchSection Section { get; }

        /// <summary>The text; empty for a statement that only binds parameters.</summary>
        public string Text { get; }

        /// <summary>The parameters it binds.</summary>
        public IReadOnlyList<BatchParameter> Parameters { get; }

        /// <summary>The tables it writes.</summary>
        public IReadOnlySet<TableName> Writes { get; }

        /// <summary>Whether running it twice has the effect of running it once.</summary>
        public bool Idempotent { get; }

        /// <summary>Reads its result sets, or null when it returns none.</summary>
        public StatementReader? Reader { get; }
    }
}
