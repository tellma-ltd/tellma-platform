// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Schema
{
    /// <summary>
    ///     A fact table for measures: one column of every numeric width and of every ordered type, and
    ///     two grouping keys, one nested inside the other.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Apart from the ledger fixture, because measures are checked by combining groups, and
    ///         that wants many rows per group and a column of every whole width — neither of which
    ///         the ledger's handful of hand-written invoices has, nor should grow to have.
    ///     </para>
    ///     <para>
    ///         Every leaf belongs to exactly one branch, so combining a branch's leaves is the same
    ///         as grouping by the branch. One leaf has a single row with every optional column
    ///         absent, so its partials are the absent ones and its counts are zero.
    ///     </para>
    /// </remarks>
    public static class MeasureFixture
    {
        /// <summary>The table the facts are stored in.</summary>
        public const string Source = "[dbo].[MeasureFacts]";

        /// <summary>How many leaves carry ordinary rows.</summary>
        public const int OrdinaryLeaves = 24;

        /// <summary>The leaf whose one row leaves every optional column absent.</summary>
        public const int SparseLeaf = OrdinaryLeaves + 1;

        /// <summary>How many rows the ordinary leaves share between them.</summary>
        private const int OrdinaryRows = 480;

        /// <summary>The seed the rows are generated from, so every run stores the same rows.</summary>
        private const int Seed = 181;

        /// <summary>The words the text column holds, alike ignoring case in places on purpose.</summary>
        private static readonly string[] Labels =
            ["Sales", "SALES", "sales", "Alpha", "alpha", "Beta", "gamma", "Delta", "delta", "Omega", "zeta"];

        /// <summary>The hierarchy positions the path column holds.</summary>
        private static readonly string[] Paths = ["/", "/1/", "/1/2/", "/1/10/", "/2/", "/2/1/", "/10/", "/-1/"];

        /// <summary>The schema that describes the table.</summary>
        public static QueryexSchema Schema { get; } = Build();

        /// <summary>The one entity: a fact.</summary>
        public static EntityDescriptor Fact { get; } = Schema.FindEntity("Fact")!;

        /// <summary>The facts, generated once from the fixed seed.</summary>
        public static IReadOnlyList<MeasureFact> Rows { get; } = Generate();

        /// <summary>The statements that create the table and store every fact.</summary>
        /// <returns>The script.</returns>
        public static string Create()
        {
            StringBuilder script = new();
            script.Append("CREATE TABLE ").Append(Source).Append(" (\n");
            List<string> columns = [];
            foreach (PropertyDescriptor property in Fact.Properties)
            {
                string collation = property.Type == QueryexType.QxString ? " COLLATE " + LedgerDdl.Collation : string.Empty;
                columns.Add("    [" + property.Column + "] " + LedgerDdl.TypeName(property.StoreType!.Value)
                    + collation + (property.IsNotNull ? " NOT NULL" : " NULL"));
            }

            script.Append(string.Join(",\n", columns)).Append("\n);\n");
            script.Append("ALTER TABLE ").Append(Source).Append(" ADD CONSTRAINT [PK_MeasureFacts] PRIMARY KEY ([Id]);\n");

            // In batches, because one statement may insert at most a thousand rows of values.
            foreach (MeasureFact[] batch in Rows.Chunk(200))
            {
                script.Append("INSERT INTO ").Append(Source).Append(" (")
                    .Append(string.Join(", ", Fact.Properties.Select(property => "[" + property.Column + "]")))
                    .Append(") VALUES\n");
                script.Append(string.Join(",\n", batch.Select(Values))).Append(";\n");
            }

            return script.ToString();
        }

        /// <summary>Builds the schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema Build()
        {
            QueryexSchemaBuilder builder = new();
            QueryexSchemaBuilder.EntityBuilder fact = builder.Entity("Fact", Source);
            fact.Key("Id", QueryexType.QxNumeric, "Id", QueryexStoreType.QxInt);

            // The grouping keys: every leaf sits in one branch.
            fact.Property("Leaf", QueryexType.QxNumeric, "Leaf", isNotNull: true, storeType: QueryexStoreType.QxInt);
            fact.Property("Branch", QueryexType.QxNumeric, "Branch", isNotNull: true, storeType: QueryexStoreType.QxInt);

            // A number of every whole width, and decimals at several scales.
            fact.Property("Tiny", QueryexType.QxNumeric, "Tiny", storeType: QueryexStoreType.QxTinyInt);
            fact.Property("Small", QueryexType.QxNumeric, "Small", storeType: QueryexStoreType.QxSmallInt);
            fact.Property("Qty", QueryexType.QxNumeric, "Qty", storeType: QueryexStoreType.QxInt);
            fact.Property("Big", QueryexType.QxNumeric, "Big", storeType: QueryexStoreType.QxBigInt);
            fact.Property("Whole", QueryexType.QxNumeric, "Whole", storeType: QueryexStoreType.QxDecimal(10, 0));
            fact.Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true, storeType: QueryexStoreType.QxDecimal(19, 4));
            fact.Property("Cost", QueryexType.QxNumeric, "Cost", storeType: QueryexStoreType.QxDecimal(19, 4));
            fact.Property("Rate", QueryexType.QxNumeric, "Rate", storeType: QueryexStoreType.QxDecimal(9, 6));
            fact.Property("Fine", QueryexType.QxNumeric, "Fine", storeType: QueryexStoreType.QxDecimal(28, 10));

            // Every other ordered type.
            fact.Property("Day", QueryexType.QxDate, "Day", isNotNull: true, storeType: QueryexStoreType.QxDate);
            fact.Property("Due", QueryexType.QxDate, "Due", storeType: QueryexStoreType.QxDate);
            fact.Property("Stamp", QueryexType.QxDateTime, "Stamp", storeType: QueryexStoreType.QxDateTime2(7));
            fact.Property("Instant", QueryexType.QxDateTimeOffset, "Instant", storeType: QueryexStoreType.QxDateTimeOffset(7));
            fact.Property("Flag", QueryexType.QxBool, "Flag", storeType: QueryexStoreType.QxBit);
            fact.Property("Label", QueryexType.QxString, "Label", storeType: QueryexStoreType.QxNVarChar(50));
            fact.Property("Node", QueryexType.QxHierarchyId, "Node", storeType: QueryexStoreType.QxHierarchyId);
            return builder.Build();
        }

        /// <summary>Generates the facts.</summary>
        /// <returns>The facts, in key order.</returns>
        private static List<MeasureFact> Generate()
        {
            Random random = new(Seed);
            List<MeasureFact> rows = [];
            for (int index = 0; index < OrdinaryRows; index++)
            {
                int leaf = 1 + (index % OrdinaryLeaves);
                DateOnly day = new DateOnly(2023, 1, 1).AddDays(random.Next(0, 900));
                rows.Add(new MeasureFact(
                    Id: index + 1,
                    Leaf: leaf,
                    Branch: BranchOf(leaf),
                    Tiny: Maybe(random, 0.1, () => (byte)random.Next(0, 256)),
                    Small: Maybe(random, 0.1, () => (short)random.Next(-300, 301)),
                    Qty: Maybe(random, 0.1, () => random.Next(-50, 1001)),
                    Big: Maybe(random, 0.1, () => (long)random.Next(-1_000_000, 1_000_001)),
                    Whole: Maybe(random, 0.1, () => (decimal)random.Next(-99_999, 100_000)),
                    Amount: Scaled(random.Next(-50_000_000, 200_000_001), 4),
                    Cost: Maybe(random, 0.3, () => Scaled(random.Next(0, 100_000_001), 4)),
                    Rate: Maybe(random, 0.2, () => Scaled(random.Next(0, 1_000_001), 6)),
                    Fine: Maybe(random, 0.2, () => Scaled(random.NextInt64(-10_000_000_000, 10_000_000_001), 10)),
                    Day: day,
                    Due: Maybe(random, 0.3, () => day.AddDays(random.Next(0, 61))),
                    Stamp: Maybe(random, 0.2, () => day.ToDateTime(TimeOnly.MinValue).AddTicks(random.NextInt64(0, TimeSpan.TicksPerDay))),
                    Instant: Maybe(random, 0.2, () => new DateTimeOffset(
                        day.ToDateTime(TimeOnly.MinValue).AddMinutes(random.Next(0, 1440)),
                        TimeSpan.FromHours(random.Next(-5, 6)))),
                    Flag: Maybe(random, 0.2, () => random.Next(0, 2) == 1),
                    Label: random.NextDouble() < 0.15 ? null : Labels[random.Next(Labels.Length)],
                    Node: random.NextDouble() < 0.15 ? null : Paths[random.Next(Paths.Length)]));
            }

            // The leaf whose optional columns are all absent.
            rows.Add(new MeasureFact(
                OrdinaryRows + 1, SparseLeaf, BranchOf(SparseLeaf), null, null, null, null, null, 1.0000m,
                null, null, null, new DateOnly(2024, 2, 29), null, null, null, null, null, null));
            return rows;
        }

        /// <summary>The branch a leaf belongs to.</summary>
        /// <param name="leaf">The leaf.</param>
        /// <returns>The branch.</returns>
        private static int BranchOf(int leaf)
        {
            return 1 + ((leaf - 1) / 5);
        }

        /// <summary>A value, or absence with the given chance.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="random">The generator.</param>
        /// <param name="absent">The chance of absence.</param>
        /// <param name="value">Makes the value.</param>
        /// <returns>The value, or null.</returns>
        private static T? Maybe<T>(Random random, double absent, Func<T> value)
            where T : struct
        {
            return random.NextDouble() < absent ? null : value();
        }

        /// <summary>A decimal number from a whole mantissa.</summary>
        /// <param name="mantissa">The digits.</param>
        /// <param name="scale">The digits after the point.</param>
        /// <returns>The number, carrying exactly that scale.</returns>
        private static decimal Scaled(long mantissa, byte scale)
        {
            ulong magnitude = (ulong)Math.Abs(mantissa);
            return new decimal(
                unchecked((int)(uint)magnitude),
                unchecked((int)(uint)(magnitude >> 32)),
                0,
                mantissa < 0,
                scale);
        }

        /// <summary>One fact as the values of an insert.</summary>
        /// <param name="row">The fact.</param>
        /// <returns>The values, in the schema's property order.</returns>
        private static string Values(MeasureFact row)
        {
            string[] values =
            [
                Number(row.Id),
                Number(row.Leaf),
                Number(row.Branch),
                Number(row.Tiny),
                Number(row.Small),
                Number(row.Qty),
                Number(row.Big),
                Number(row.Whole),
                Number(row.Amount),
                Number(row.Cost),
                Number(row.Rate),
                Number(row.Fine),
                Quoted(row.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Quoted(row.Due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Quoted(row.Stamp?.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)),
                Quoted(row.Instant?.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)),
                row.Flag is bool flag ? (flag ? "1" : "0") : "NULL",
                row.Label is null ? "NULL" : "N" + Quoted(row.Label),
                Quoted(row.Node),
            ];

            return "(" + string.Join(", ", values) + ")";
        }

        /// <summary>A number as a literal.</summary>
        /// <typeparam name="T">The number's type.</typeparam>
        /// <param name="value">The number, or null.</param>
        /// <returns>The literal.</returns>
        private static string Number<T>(T? value)
            where T : struct, IFormattable
        {
            return value is T present ? present.ToString(null, CultureInfo.InvariantCulture) : "NULL";
        }

        /// <summary>A number as a literal.</summary>
        /// <typeparam name="T">The number's type.</typeparam>
        /// <param name="value">The number.</param>
        /// <returns>The literal.</returns>
        private static string Number<T>(T value)
            where T : struct, IFormattable
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }

        /// <summary>Text as a quoted literal.</summary>
        /// <param name="text">The text, or null.</param>
        /// <returns>The literal.</returns>
        private static string Quoted(string? text)
        {
            return text is null ? "NULL" : "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
        }
    }

    /// <summary>One stored fact.</summary>
    /// <param name="Id">The key.</param>
    /// <param name="Leaf">The fine grouping key.</param>
    /// <param name="Branch">The coarse grouping key, which the leaf determines.</param>
    /// <param name="Tiny">A one-byte whole number.</param>
    /// <param name="Small">A two-byte whole number.</param>
    /// <param name="Qty">A four-byte whole number.</param>
    /// <param name="Big">An eight-byte whole number.</param>
    /// <param name="Whole">A whole number stored as a decimal.</param>
    /// <param name="Amount">A decimal with four places.</param>
    /// <param name="Cost">An optional decimal with four places.</param>
    /// <param name="Rate">A decimal with six places.</param>
    /// <param name="Fine">A decimal with ten places.</param>
    /// <param name="Day">A calendar date.</param>
    /// <param name="Due">An optional calendar date.</param>
    /// <param name="Stamp">A date and time.</param>
    /// <param name="Instant">An instant with its offset.</param>
    /// <param name="Flag">A truth value.</param>
    /// <param name="Label">Text.</param>
    /// <param name="Node">A hierarchy position's path.</param>
    public sealed record MeasureFact(
        int Id,
        int Leaf,
        int Branch,
        byte? Tiny,
        short? Small,
        int? Qty,
        long? Big,
        decimal? Whole,
        decimal Amount,
        decimal? Cost,
        decimal? Rate,
        decimal? Fine,
        DateOnly Day,
        DateOnly? Due,
        DateTime? Stamp,
        DateTimeOffset? Instant,
        bool? Flag,
        string? Label,
        string? Node);
}
