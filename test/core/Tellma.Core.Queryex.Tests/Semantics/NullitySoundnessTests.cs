// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.Tests.Semantics
{
    /// <summary>
    ///     Checks what the compiler concluded about absence against what actually happens.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the check the whole nullity analysis exists to be held to. Guards are left
    ///         out of the emitted SQL wherever the analysis says a value is always there, so a claim
    ///         that turns out to be false does not merely mislead — it drops rows from a report and
    ///         from an access-control decision, silently.
    ///     </para>
    ///     <para>
    ///         Both directions are checked. The permissive mistake costs rows; the other one folds a
    ///         subexpression away that should have been evaluated, which loses rows just as quietly.
    ///     </para>
    /// </remarks>
    public sealed class NullitySoundnessTests
    {
        /// <summary>Expressions written for this check rather than for any other.</summary>
        private static readonly string[] Hard =
        [
            "coalesce(Rate, Amount)",
            "coalesce(Rate, Amount, 0)",
            "coalesce(Memo, Notes)",
            "if(IsPosted, Amount, Rate)",
            "if(true, Amount, Rate)",
            "if(false, Amount, Rate)",
            "Customer.Name",
            "Customer.Manager.Name",
            "Centre.Region.Name",
            "Customer.Region.Name",
            "Amount + Rate",
            "Amount * 2",
            "Memo || Notes",
            "Memo || 'x'",
            "cast(Notes, 'date')",
            "cast(Amount, 'string')",
            "length(Memo)",
            "upper(Memo)",
            "year(PostingDate)",
            "year(local(PostedAt))",
            "startOfMonth(PostingDate)",
            "diffDays(PostingDate, DueDate)",
            "diffDays(PostingDate, PostingDate)",
            "addDays(PostedAt, 1)",
            "round(Rate, 2)",
            "abs(Amount)",
            "contains(Memo, 'a')",
            "Rate = 1.25",
            "Rate != 1.25",
            "Memo = Notes",
            "Rate in (1, null)",
            "Rate is null",
            "not IsApproved",
            "IsApproved and IsPosted",
            "IsApproved or IsPosted",
            "descendantOf(Account.Concept, 'Assets')",
            "me()",
            "today()",
            "now()",
        ];

        /// <summary>Every expression that is swept, as theory arguments.</summary>
        /// <returns>The expressions.</returns>
        public static TheoryData<string> Expressions()
        {
            TheoryData<string> data = [];
            foreach (string expression in Hard)
            {
                data.Add(expression);
            }

            return data;
        }

        /// <summary>Every claim about absence holds for every row of the fixture.</summary>
        /// <param name="expression">The expression text.</param>
        [Theory]
        [MemberData(nameof(Expressions))]
        public void Claims_HoldForEveryRow(string expression)
        {
            IReadOnlyList<string> violations = ReferenceSemantics.NullityViolations(
                expression,
                LedgerFixture.Invoice,
                QueryexMode.Value,
                InterpreterContext.Fixed);

            Assert.Empty(violations);
        }

        /// <summary>Aggregations claim what they turn out to do, grouped and ungrouped alike.</summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="grouped">Whether the enclosing query groups by anything.</param>
        [Theory]
        [InlineData("sum(Amount)", true)]
        [InlineData("sum(Amount)", false)]
        [InlineData("sum(Rate)", true)]
        [InlineData("sum(Amount, IsPosted)", true)]
        [InlineData("count()", true)]
        [InlineData("count(Rate)", true)]
        [InlineData("count(Amount, IsPosted)", true)]
        [InlineData("avg(Amount)", true)]
        [InlineData("min(PostingDate)", true)]
        [InlineData("max(Memo)", true)]
        public void Aggregations_ClaimWhatTheyDo(string expression, bool grouped)
        {
            IReadOnlyList<string> violations = ReferenceSemantics.NullityViolations(
                expression,
                LedgerFixture.Invoice,
                QueryexMode.Aggregate,
                InterpreterContext.Fixed,
                hasGroupingKeys: grouped);

            Assert.Empty(violations);
        }

        /// <summary>Without a signed-in user, the claim that there is no identifier holds.</summary>
        [Fact]
        public void WithoutAUser_TheIdentifierIsNeverThere()
        {
            IReadOnlyList<string> violations = ReferenceSemantics.NullityViolations(
                "me()",
                LedgerFixture.Invoice,
                QueryexMode.Value,
                InterpreterContext.Fixed with { UserId = null },
                hasUser: false);

            Assert.Empty(violations);
        }

        /// <summary>A parameter declared as always supplied claims so truthfully.</summary>
        [Fact]
        public void DeclaredParameters_ClaimWhatTheyAreGiven()
        {
            InterpreterContext context = InterpreterContext.Fixed with
            {
                Parameters = new Dictionary<string, QxValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["given"] = QxValue.Number(new SqlDecimal(5)),
                    ["maybe"] = QxValue.Absent(QueryexType.QxNumeric),
                },
            };

            IReadOnlyList<string> violations = ReferenceSemantics.NullityViolations(
                "Amount + @given",
                LedgerFixture.Invoice,
                QueryexMode.Value,
                context,
                declarations: [new QueryexParameterDeclaration("given", QueryexType.QxNumeric, IsNotNull: true)]);

            Assert.Empty(violations);
        }

        /// <summary>Every expression the corpus compiles is swept too.</summary>
        /// <remarks>
        ///     The corpus grows for other reasons, and sweeping it here means every case added for a
        ///     typing or a parsing question also becomes one more row of evidence about absence.
        /// </remarks>
        [Fact]
        public void EveryCorpusExpression_IsSound()
        {
            List<string> violations = [];
            foreach (ExpressionCase entry in ExpressionCorpus.All)
            {
                if (!ReferenceSemantics.CanEvaluate(entry))
                {
                    continue;
                }

                InterpreterContext context = entry.HasUser
                    ? InterpreterContext.Fixed
                    : InterpreterContext.Fixed with { UserId = null };

                violations.AddRange(ReferenceSemantics.NullityViolations(
                    entry.Text,
                    LedgerFixture.Invoice,
                    entry.Mode,
                    context,
                    entry.HasGroupingKeys,
                    entry.HasUser));
            }

            Assert.Empty(violations);
        }
    }
}
