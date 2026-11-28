// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     The base of the data layer's exceptions: a batch failed in a way the executor classified.
    ///     The web layer never maps these; the service pipeline translates each into the platform's
    ///     closed set of outcomes, and anything it does not translate is a 500.
    /// </summary>
    /// <remarks>
    ///     Thrown as itself for a server error the data layer has no finer mapping for; its
    ///     <see cref="Exception.InnerException" /> is then the provider's exception.
    /// </remarks>
    /// <param name="message">English text for logs.</param>
    /// <param name="statementOrdinal">The ordinal of the batch statement that raised the error; null when no statement did.</param>
    /// <param name="innerException">The provider's exception, if any.</param>
    public class DataAccessException(string message, int? statementOrdinal = null, Exception? innerException = null)
        : Exception(message, innerException)
    {
        /// <summary>
        ///     The batch ordinal of the statement that raised the error, resolved from the error's line
        ///     in the batch text; null when the error came from the batch's frame (the schema guard,
        ///     the persist re-checks), from a trigger or procedure a statement called, or from no
        ///     statement at all.
        /// </summary>
        public int? StatementOrdinal { get; } = statementOrdinal;

        /// <summary>Names the statement an error came from, for a message.</summary>
        /// <param name="statementOrdinal">The statement's ordinal, or null.</param>
        /// <returns><c>Batch statement 3</c>, or <c>The batch</c> when no statement is known.</returns>
        private protected static string Describe(int? statementOrdinal)
        {
            return statementOrdinal is int ordinal
                ? string.Create(CultureInfo.InvariantCulture, $"Batch statement {ordinal}")
                : "The batch";
        }
    }
}
