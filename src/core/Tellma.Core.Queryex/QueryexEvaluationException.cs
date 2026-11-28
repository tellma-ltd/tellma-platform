// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     A measure could not be computed for a cell, where the backend would have raised an error
    ///     computing it over the same rows.
    /// </summary>
    /// <remarks>
    ///     The run fails as a statement fails when one of its groups divides by zero; a host reports
    ///     the failure rather than leaving the cell blank.
    /// </remarks>
    public sealed class QueryexEvaluationException : Exception
    {
        /// <summary>The code of a division by zero.</summary>
        public const string DivisionByZero = "QX6001";

        /// <summary>
        ///     The code of an arithmetic overflow: a result past 38 digits, a whole number past its
        ///     type, or a total past 38 digits.
        /// </summary>
        public const string ArithmeticOverflow = "QX6002";

        /// <summary>Initializes the exception.</summary>
        /// <param name="code">The code: <see cref="DivisionByZero" /> or <see cref="ArithmeticOverflow" />.</param>
        /// <param name="message">What went wrong, for logs.</param>
        internal QueryexEvaluationException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        /// <summary>
        ///     The code: <see cref="DivisionByZero" /> for a division by zero, or
        ///     <see cref="ArithmeticOverflow" /> for an arithmetic overflow.
        /// </summary>
        public string Code { get; }

        /// <summary>A division by zero.</summary>
        /// <returns>The exception.</returns>
        internal static QueryexEvaluationException DividedByZero()
        {
            return new QueryexEvaluationException(DivisionByZero, "A measure divided by zero.");
        }

        /// <summary>An arithmetic overflow.</summary>
        /// <returns>The exception.</returns>
        internal static QueryexEvaluationException Overflowed()
        {
            return new QueryexEvaluationException(
                ArithmeticOverflow,
                "A measure's arithmetic overflowed the type its value is held in.");
        }

        /// <summary>A total past the 38 digits a decimal holds.</summary>
        /// <returns>The exception.</returns>
        internal static QueryexEvaluationException TotalOverflowed()
        {
            return new QueryexEvaluationException(
                ArithmeticOverflow,
                "A combined total exceeds the 38 digits a decimal holds.");
        }
    }
}
