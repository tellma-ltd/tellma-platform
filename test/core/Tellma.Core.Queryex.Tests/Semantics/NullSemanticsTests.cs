// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.Tests.Semantics
{
    /// <summary>
    ///     What the language says happens when a value is not there.
    /// </summary>
    /// <remarks>
    ///     Checked against the second implementation, which reads the rules directly, over rows that
    ///     really are missing values. Every row of the fixture that carries no optional values is
    ///     one of these cases in practice, so the answers here are the answers a report gets.
    /// </remarks>
    public sealed class NullSemanticsTests
    {
        /// <summary>A row of the fixture where nothing optional is there.</summary>
        private const int EmptyRow = 2;

        /// <summary>A row of the fixture where everything optional is there.</summary>
        private const int FullRow = 1;

        /// <summary>Evaluates one expression against one row of the fixture.</summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="row">The row's key.</param>
        /// <returns>Its value.</returns>
        private static QxValue Evaluate(string expression, int row)
        {
            IReadOnlyList<RowReading> readings = ReferenceSemantics.Evaluate(
                expression,
                LedgerFixture.Invoice,
                QueryexMode.Value,
                InterpreterContext.Fixed);

            return readings.Single(reading => reading.Row == row).Values[0];
        }

        /// <summary>Asserts what one expression evaluates to for one row.</summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="row">The row's key.</param>
        /// <param name="expected">The truth value expected.</param>
        private static void Holds(string expression, int row, bool expected)
        {
            QxValue value = Evaluate(expression, row);
            Assert.False(value.IsAbsent, expression + " should be a truth value");
            Assert.Equal(expected, value.AsFlag);
        }

        /// <summary>A missing value is not equal to one that is there.</summary>
        [Fact]
        public void Equality_AgainstAMissingValue_IsFalse()
        {
            Holds("Rate = 1.25", EmptyRow, false);
            Holds("Rate = 1.25", FullRow, true);
        }

        /// <summary>Inequality is exactly the complement of equality, missing values and all.</summary>
        [Fact]
        public void Inequality_IsTheComplementOfEquality()
        {
            Holds("Rate != 1.25", EmptyRow, true);
            Holds("Rate != 1.25", FullRow, false);
        }

        /// <summary>Two missing values are equal to each other.</summary>
        [Fact]
        public void TwoMissingValues_AreEqual()
        {
            Holds("Memo = Notes", EmptyRow, true);
        }

        /// <summary>A missing value is ordered against nothing.</summary>
        [Fact]
        public void Ordering_AgainstAMissingValue_IsFalse()
        {
            Holds("Rate < 1000", EmptyRow, false);
            Holds("Rate > 1000", EmptyRow, false);
            Holds("Rate <= 1000", EmptyRow, false);
            Holds("Rate >= 1000", EmptyRow, false);
        }

        /// <summary>Asking whether a missing value is missing says yes.</summary>
        [Fact]
        public void AbsenceTest_SeesAbsence()
        {
            Holds("Rate is null", EmptyRow, true);
            Holds("Rate is not null", EmptyRow, false);
        }

        /// <summary>A missing value belongs to a list that has a missing value in it.</summary>
        [Fact]
        public void Membership_MatchesAMissingElement()
        {
            Holds("Rate in (1, null)", EmptyRow, true);
            Holds("Rate in (1, 2)", EmptyRow, false);
        }

        /// <summary>Negating a comparison against a missing value gives the other answer.</summary>
        /// <remarks>
        ///     The property that makes the whole design work: because comparison is never undecided,
        ///     wrapping it in a negation cannot lose the row.
        /// </remarks>
        [Fact]
        public void Negation_OfAComparisonAgainstAbsence_KeepsTheRow()
        {
            Holds("not (Rate = 1.25)", EmptyRow, true);
            Holds("not (Rate > 1000)", EmptyRow, true);
        }

        /// <summary>Searching a missing value finds nothing rather than answering nothing.</summary>
        [Fact]
        public void Matching_AgainstAMissingValue_IsFalse()
        {
            Holds("contains(Memo, 'x')", EmptyRow, false);
            Holds("startsWith(Memo, 'x')", EmptyRow, false);
            Holds("endsWith(Memo, 'x')", EmptyRow, false);
            Holds("not contains(Memo, 'x')", EmptyRow, true);
        }

        /// <summary>Joining text to a missing value yields a missing value.</summary>
        [Fact]
        public void Concatenation_WithAMissingValue_IsMissing()
        {
            Assert.True(Evaluate("Memo || 'x'", EmptyRow).IsAbsent);
        }

        /// <summary>Arithmetic with a missing value yields a missing value.</summary>
        [Fact]
        public void Arithmetic_WithAMissingValue_IsMissing()
        {
            Assert.True(Evaluate("Rate + 1", EmptyRow).IsAbsent);
            Assert.True(Evaluate("Rate * Amount", EmptyRow).IsAbsent);
        }

        /// <summary>A missing truth value reads as false, even under a negation.</summary>
        [Fact]
        public void AMissingTruthValue_ReadsAsFalse()
        {
            Holds("IsApproved and true", EmptyRow, false);
            Holds("not IsApproved", EmptyRow, true);
            Holds("IsApproved or true", EmptyRow, true);
        }

        /// <summary>The first value that is there wins, and nothing is there when none is.</summary>
        [Fact]
        public void FirstPresentWins()
        {
            Assert.Equal("0", Evaluate("coalesce(Rate, 0)", EmptyRow).AsNumber.ToString());
            Assert.True(Evaluate("coalesce(Memo, Notes)", EmptyRow).IsAbsent);
        }

        /// <summary>Trailing spaces do not distinguish one value from another.</summary>
        [Fact]
        public void Comparison_IgnoresTrailingSpaces()
        {
            Holds("Ref = 'REF-1'", FullRow, true);
        }

        /// <summary>Searching does see trailing spaces, unlike comparison.</summary>
        [Fact]
        public void Matching_SeesTrailingSpaces()
        {
            Holds("endsWith(Ref, '1')", FullRow, false);
            Holds("contains(Ref, '1 ')", FullRow, true);
        }

        /// <summary>Text comparison ignores case, as the declared collation does.</summary>
        [Fact]
        public void Comparison_IgnoresCase()
        {
            Holds("Memo = 'ALPHA MEMO'", FullRow, true);
            Holds("contains(Memo, 'ALPHA')", FullRow, true);
        }

        /// <summary>Length stops at the last character that is not a space.</summary>
        [Fact]
        public void Length_IgnoresTrailingSpaces()
        {
            Assert.Equal("5", Evaluate("length(Ref)", FullRow).AsNumber.ToString());
        }
    }
}
