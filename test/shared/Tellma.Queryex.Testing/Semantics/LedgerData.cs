// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>One row of the fixture, as the second implementation sees it.</summary>
    /// <param name="Entity">The entity the row belongs to.</param>
    /// <param name="Values">Its values, by logical property name.</param>
    public sealed record LedgerRow(EntityDescriptor Entity, FrozenDictionary<string, QxValue> Values)
    {
        /// <summary>
        ///     The values of the columns the schema leaves out, by physical column name. Every one of
        ///     them is text, and absent where the row has none.
        /// </summary>
        public FrozenDictionary<string, QxValue> Undeclared { get; init; } =
            FrozenDictionary<string, QxValue>.Empty;

        /// <summary>One value of the row.</summary>
        /// <param name="property">The logical property name.</param>
        /// <returns>The value, or absence when the row has none.</returns>
        public QxValue this[string property] =>
            Values.TryGetValue(property, out QxValue? value)
                ? value
                : QxValue.Absent(QueryexType.QxString);
    }

    /// <summary>
    ///     The rows the fixture holds, and the statements that put the same rows on a server.
    /// </summary>
    /// <remarks>
    ///     One declaration, read two ways. If the rows a differential run compares against were
    ///     written twice — once for memory and once for the server — the first divergence found
    ///     would be between the two copies rather than between the two implementations of the
    ///     language.
    /// </remarks>
    public static class LedgerData
    {
        /// <summary>Text that is deliberately padded, to exercise the padding rules.</summary>
        private const string PaddedRef = "REF-1     ";

        /// <summary>The rows, by entity name.</summary>
        private static readonly FrozenDictionary<string, IReadOnlyList<LedgerRow>> Tables = Build();

        /// <summary>Every row of one entity, in key order.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The rows.</returns>
        public static IReadOnlyList<LedgerRow> Rows(EntityDescriptor entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            return Tables.TryGetValue(entity.Name, out IReadOnlyList<LedgerRow>? rows) ? rows : [];
        }

        /// <summary>The row of an entity with a given key, when there is one.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="key">The key.</param>
        /// <returns>The row, or null.</returns>
        public static LedgerRow? Find(EntityDescriptor entity, QxValue key)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(key);

            if (key.IsAbsent)
            {
                return null;
            }

            foreach (LedgerRow row in Rows(entity))
            {
                QxValue candidate = row[entity.Key.Name];
                if (!candidate.IsAbsent && candidate.AsNumber == key.AsNumber)
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>The statements that put the same rows on a server.</summary>
        /// <returns>The script.</returns>
        public static string Insert()
        {
            StringBuilder script = new();
            foreach (EntityDescriptor entity in LedgerFixture.Schema.Entities)
            {
                foreach (LedgerRow row in Rows(entity))
                {
                    AppendInsert(script, entity, row);
                }
            }

            return script.ToString();
        }

        /// <summary>Every piece of text any row holds, for the repertoire guard.</summary>
        /// <returns>The values.</returns>
        public static IEnumerable<string> AllText()
        {
            foreach (IReadOnlyList<LedgerRow> rows in Tables.Values)
            {
                foreach (LedgerRow row in rows)
                {
                    foreach (QxValue value in row.Values.Values.Concat(row.Undeclared.Values))
                    {
                        if (!value.IsAbsent && value.Type == QueryexType.QxString)
                        {
                            yield return value.AsText;
                        }
                    }
                }
            }
        }

        /// <summary>Builds every table.</summary>
        /// <returns>The rows, by entity name.</returns>
        private static FrozenDictionary<string, IReadOnlyList<LedgerRow>> Build()
        {
            Dictionary<string, IReadOnlyList<LedgerRow>> tables = new(StringComparer.Ordinal)
            {
                ["Region"] = Regions(),
                ["Party"] = Parties(),
                ["Centre"] = Centres(),
                ["Account"] = Accounts(),
                ["Invoice"] = Invoices(),
                ["InvoiceLine"] = Lines(),
            };

            return tables.ToFrozenDictionary(StringComparer.Ordinal);
        }

        /// <summary>The regions.</summary>
        /// <returns>The rows.</returns>
        private static IReadOnlyList<LedgerRow> Regions()
        {
            EntityDescriptor region = LedgerFixture.Schema.FindEntity("Region")!;
            return
            [
                Row(region, ("Id", QxValue.Number(1)), ("Name", QxValue.Text("North")), ("Code", Fixed("N", 10))),
                Row(region, ("Id", QxValue.Number(2)), ("Name", QxValue.Text("South")), ("Code", Fixed("S", 10))),
            ];
        }

        /// <summary>The parties.</summary>
        /// <returns>The rows.</returns>
        private static IReadOnlyList<LedgerRow> Parties()
        {
            EntityDescriptor party = LedgerFixture.Schema.FindEntity("Party")!;
            return
            [
                Row(
                    party,
                    ("Id", QxValue.Number(1)),
                    ("Name", QxValue.Text("Acme")),
                    ("Gender", Fixed("M", 1)),
                    ("TaxId", QxValue.Text("TX-1")),
                    ("RegionId", QxValue.Number(1)),
                    ("ManagerId", QxValue.Absent(QueryexType.QxNumeric))),
                Row(
                    party,
                    ("Id", QxValue.Number(2)),
                    ("Name", QxValue.Text("beta")),
                    ("Gender", QxValue.Absent(QueryexType.QxString)),
                    ("TaxId", QxValue.Absent(QueryexType.QxString)),
                    ("RegionId", QxValue.Number(1)),
                    ("ManagerId", QxValue.Number(1))),
                Row(
                    party,
                    ("Id", QxValue.Number(3)),
                    ("Name", QxValue.Text("Gamma")),
                    ("Gender", Fixed("F", 1)),
                    ("TaxId", QxValue.Text("TX-3")),
                    ("RegionId", QxValue.Number(2)),
                    ("ManagerId", QxValue.Number(1))),
            ];
        }

        /// <summary>The centres.</summary>
        /// <returns>The rows.</returns>
        private static IReadOnlyList<LedgerRow> Centres()
        {
            EntityDescriptor centre = LedgerFixture.Schema.FindEntity("Centre")!;
            return
            [
                Row(centre, ("Id", QxValue.Number(1)), ("Name", QxValue.Text("Ops")), ("RegionId", QxValue.Number(1))),
                Row(centre, ("Id", QxValue.Number(2)), ("Name", QxValue.Text("Sales")), ("RegionId", QxValue.Number(2))),
            ];
        }

        /// <summary>The accounts, which form a two-rooted hierarchy.</summary>
        /// <returns>The rows.</returns>
        private static IReadOnlyList<LedgerRow> Accounts()
        {
            EntityDescriptor account = LedgerFixture.Schema.FindEntity("Account")!;
            return
            [
                Account(account, 1, "Assets", "Assets", "/1/", null),
                Account(account, 2, "Cash", "Ledger", "/1/1/", 1),

                // The same label as the row above it, which is why asking for ancestry by label has
                // to be refused: the key would name two rows.
                Account(account, 3, "Bank", "Ledger", "/1/2/", 1),
                Account(account, 4, "Equity", "Equity", "/2/", null),
                Account(account, 5, "Capital", "Capital", "/2/1/", 4),
            ];
        }

        /// <summary>One account.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="id">Its key.</param>
        /// <param name="concept">Its unique concept.</param>
        /// <param name="label">Its label, which need not be unique.</param>
        /// <param name="node">Its position in the hierarchy.</param>
        /// <param name="parent">Its parent's key, when it has one.</param>
        /// <returns>The row.</returns>
        private static LedgerRow Account(
            EntityDescriptor entity,
            int id,
            string concept,
            string label,
            string node,
            int? parent)
        {
            return Row(
                entity,
                ("Id", QxValue.Number(id)),
                ("Concept", QxValue.Text(concept)),
                ("Label", QxValue.Text(label)),
                ("Node", QxValue.Node(node)),
                ("ParentId", parent is int key
                    ? QxValue.Number(key)
                    : QxValue.Absent(QueryexType.QxNumeric)));
        }

        /// <summary>The invoices, which carry an absent value in every column that permits one.</summary>
        /// <returns>The rows.</returns>
        private static IReadOnlyList<LedgerRow> Invoices()
        {
            EntityDescriptor invoice = LedgerFixture.Invoice;
            return
            [
                Invoice(invoice, 1, everything: true),
                Invoice(invoice, 2, everything: false),
                Invoice(invoice, 3, everything: true),
                Invoice(invoice, 4, everything: false),
                Invoice(invoice, 5, everything: true),
                Invoice(invoice, 6, everything: false),
            ];
        }

        /// <summary>One invoice.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="id">Its key.</param>
        /// <param name="everything">Whether the columns that permit absence carry a value.</param>
        /// <returns>The row.</returns>
        /// <remarks>
        ///     Alternating rows leave every optional column empty, so that a check which claims a
        ///     value is always present has to survive rows where nothing optional is.
        /// </remarks>
        private static LedgerRow Invoice(EntityDescriptor entity, int id, bool everything)
        {
            DateOnly posting = new DateOnly(2024, 1, 1).AddDays(id * 40);
            DateTimeOffset approved = new(
                posting.Year,
                posting.Month,
                posting.Day,
                6 + id,
                30,
                0,
                TimeSpan.FromHours(3));

            var amount = SqlDecimal.Parse(
                ((id - 3) * 125).ToString(CultureInfo.InvariantCulture) + ".2500");

            return Row(
                entity,
                ("Id", QxValue.Number(id)),
                ("PostingDate", QxValue.Date(posting)),
                ("DueDate", everything
                    ? QxValue.Date(posting.AddDays(30))
                    : QxValue.Absent(QueryexType.QxDate)),
                ("PostedAt", everything
                    ? QxValue.Instant(approved.AddHours(2))
                    : QxValue.Absent(QueryexType.QxDateTimeOffset)),
                ("ApprovedAt", QxValue.Instant(approved)),
                ("DueOn", everything
                    ? QxValue.Moment(new DateTime(posting.AddDays(30), new TimeOnly(9, 15), DateTimeKind.Unspecified))
                    : QxValue.Absent(QueryexType.QxDateTime)),
                ("PostedOn", QxValue.Moment(new DateTime(posting, new TimeOnly(8, 45), DateTimeKind.Unspecified))),
                ("Amount", QxValue.Number(amount)),
                ("Rate", everything
                    ? QxValue.Number(SqlDecimal.Parse("1.250000"))
                    : QxValue.Absent(QueryexType.QxNumeric)),
                ("Memo", everything
                    ? QxValue.Text(id % 4 == 1 ? "alpha memo" : "ALPHA memo")
                    : QxValue.Absent(QueryexType.QxString)),
                ("Code", QxValue.Text("INV-" + id.ToString(CultureInfo.InvariantCulture))),
                ("Ref", everything ? Fixed(PaddedRef.Trim(), 10) : QxValue.Absent(QueryexType.QxString)),
                // Shaped like a date on purpose, so that reading text back as a date has something
                // to read: the language converts strictly, and there is no forgiving form to fall
                // back on when the text is not a date at all.
                ("Notes", everything
                    ? QxValue.Text(posting.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    : QxValue.Absent(QueryexType.QxString)),
                ("IsPosted", QxValue.Flag(id % 2 == 1)),
                ("IsApproved", everything
                    ? QxValue.Flag(id % 3 == 0)
                    : QxValue.Absent(QueryexType.QxBool)),
                ("Gender", everything ? Fixed(id % 2 == 1 ? "F" : "M", 1) : QxValue.Absent(QueryexType.QxString)),
                ("ExternalId", everything
                    ? QxValue.Identifier(Identifier(id))
                    : QxValue.Absent(QueryexType.QxGuid)),
                ("BatchId", QxValue.Identifier(Identifier(100 + id))),
                ("Count", QxValue.Number(id * 3)),
                ("not", QxValue.Flag(id % 2 == 0)),
                ("Location", everything
                    ? QxValue.Spatial("POINT(36.8 -1.3)")
                    : QxValue.Absent(QueryexType.QxGeography)),
                ("Territory", QxValue.Spatial("POINT(38.7 9.0)")),
                ("AltNode", everything
                    ? QxValue.Node("/" + id.ToString(CultureInfo.InvariantCulture) + "/")
                    : QxValue.Absent(QueryexType.QxHierarchyId)),
                ("CreatedById", QxValue.Number(1 + (id % 3))),
                ("CustomerId", everything
                    ? QxValue.Number(1 + (id % 3))
                    : QxValue.Absent(QueryexType.QxNumeric)),
                ("CentreId", QxValue.Number(1 + (id % 2))),
                ("AccountId", QxValue.Number(1 + (id % 5)))) with
            {
                // Opaque text the schema never declares: present on some rows and absent on others,
                // and the two columns absent on different rows, so reading one in place of the
                // other shows.
                Undeclared = new Dictionary<string, QxValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ExtrasJson"] = everything
                        ? QxValue.Text("{\"line\":" + id.ToString(CultureInfo.InvariantCulture) + "}")
                        : QxValue.Absent(QueryexType.QxString),
                    ["AuditJson"] = id % 3 == 0
                        ? QxValue.Absent(QueryexType.QxString)
                        : QxValue.Text("{\"by\":\"user-" + (id % 3).ToString(CultureInfo.InvariantCulture) + "\"}"),
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            };
        }

        /// <summary>The invoice lines, which point at their invoices.</summary>
        /// <returns>The rows.</returns>
        /// <remarks>
        ///     Invoices 2 and 6 have no lines, so a semi-join has rows it must leave out whatever its
        ///     criterion says. Each other invoice has lines its criteria tell apart: a negative and a
        ///     zero amount, a line with no party, and lines created by different users, so one compiled
        ///     statement answers differently for two of them.
        /// </remarks>
        private static IReadOnlyList<LedgerRow> Lines()
        {
            EntityDescriptor line = LedgerFixture.InvoiceLine;
            return
            [
                Line(line, 1, invoice: 1, "10.0000", "first", party: 1, createdBy: 1),
                Line(line, 2, invoice: 1, "-5.0000", null, party: null, createdBy: 2),
                Line(line, 3, invoice: 3, "20.0000", "third", party: 3, createdBy: 2),
                Line(line, 4, invoice: 3, "0.0000", "zero", party: null, createdBy: 3),
                Line(line, 5, invoice: 4, "40.0000", null, party: 2, createdBy: 1),
                Line(line, 6, invoice: 5, "7.5000", "fifth", party: 1, createdBy: 2),
                Line(line, 7, invoice: 5, "12.2500", null, party: 3, createdBy: 3),
            ];
        }

        /// <summary>One invoice line.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="id">Its key.</param>
        /// <param name="invoice">The invoice it belongs to.</param>
        /// <param name="amount">Its amount, written at the column's scale.</param>
        /// <param name="memo">Its memo, when it has one.</param>
        /// <param name="party">The party it names, when it names one.</param>
        /// <param name="createdBy">The user who created it.</param>
        /// <returns>The row.</returns>
        private static LedgerRow Line(
            EntityDescriptor entity,
            int id,
            int invoice,
            string amount,
            string? memo,
            int? party,
            int createdBy)
        {
            return Row(
                entity,
                ("Id", QxValue.Number(id)),
                ("InvoiceId", QxValue.Number(invoice)),
                ("Amount", QxValue.Number(SqlDecimal.Parse(amount))),
                ("Memo", memo is null ? QxValue.Absent(QueryexType.QxString) : QxValue.Text(memo)),
                ("PartyId", party is int key ? QxValue.Number(key) : QxValue.Absent(QueryexType.QxNumeric)),
                ("CreatedById", QxValue.Number(createdBy)));
        }

        /// <summary>A stable identifier for a row, so a run produces the same values as the last.</summary>
        /// <param name="seed">The row's number.</param>
        /// <returns>The identifier.</returns>
        private static Guid Identifier(int seed)
        {
            Span<byte> bytes = stackalloc byte[16];
            bytes.Clear();
            bytes[0] = (byte)seed;
            bytes[15] = (byte)(seed + 1);
            return new Guid(bytes);
        }

        /// <summary>Text stored in a fixed-width column, padded the way storage pads it.</summary>
        /// <param name="text">The text.</param>
        /// <param name="width">The column's width.</param>
        /// <returns>The value.</returns>
        private static QxValue Fixed(string text, int width)
        {
            return QxValue.Text(text.PadRight(width));
        }

        /// <summary>Builds one row.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="values">Its values, by logical property name.</param>
        /// <returns>The row.</returns>
        private static LedgerRow Row(EntityDescriptor entity, params (string Name, QxValue Value)[] values)
        {
            Dictionary<string, QxValue> map = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, QxValue value) in values)
            {
                map[name] = value;
            }

            return new LedgerRow(entity, map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Writes the statement that puts one row on a server.</summary>
        /// <param name="script">The script being built.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="row">The row.</param>
        private static void AppendInsert(StringBuilder script, EntityDescriptor entity, LedgerRow row)
        {
            // The declared columns, then the ones the schema leaves out, in the order the table
            // declares them.
            List<string> columns = [];
            List<string> values = [];
            foreach (PropertyDescriptor property in entity.Properties)
            {
                columns.Add("[" + property.Column + "]");
                values.Add(Rendered(row[property.Name]));
            }

            foreach (UndeclaredColumn undeclared in LedgerFixture.UndeclaredColumns(entity))
            {
                columns.Add("[" + undeclared.Name + "]");
                values.Add(Rendered(row.Undeclared.GetValueOrDefault(undeclared.Name)
                    ?? QxValue.Absent(QueryexType.QxString)));
            }

            script.Append("INSERT INTO ").Append(entity.Source).Append(" (")
                .Append(string.Join(", ", columns))
                .Append(") VALUES (")
                .Append(string.Join(", ", values))
                .Append(");\n");
        }

        /// <summary>Writes one value the way a statement carries it, for a host's own statements.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The rendered value, or <c>NULL</c> when it is absent.</returns>
        /// <remarks>
        ///     What a test uses to fill a table of keys the same way the fixture fills its own tables,
        ///     so the two cannot read one value differently.
        /// </remarks>
        public static string Literal(QxValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            return Rendered(value);
        }

        /// <summary>Writes one value the way a statement carries it.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The rendered value.</returns>
        /// <remarks>
        ///     Every form here is language-neutral, so what the fixture holds does not depend on how
        ///     the connection running the statement happens to be configured.
        /// </remarks>
        private static string Rendered(QxValue value)
        {
            return value.IsAbsent ? "NULL" : Written(value);
        }

        /// <summary>Writes one present value the way a statement carries it.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The rendered value.</returns>
        private static string Written(QxValue value)
        {
            return value.Type switch
            {
                QueryexType.QxBool => value.AsFlag ? "1" : "0",
                QueryexType.QxNumeric => value.AsNumber.ToString(),
                QueryexType.QxString => "N'" + value.AsText.Replace("'", "''", StringComparison.Ordinal) + "'",
                QueryexType.QxGuid => "CAST('"
                    + value.AsIdentifier.ToString("D", CultureInfo.InvariantCulture)
                    + "' AS uniqueidentifier)",
                QueryexType.QxDate => "CONVERT(date, '"
                    + value.AsDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "', 23)",
                QueryexType.QxDateTime => "CONVERT(datetime2(7), '"
                    + value.AsMoment.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)
                    + "', 126)",
                QueryexType.QxDateTimeOffset => "CONVERT(datetimeoffset(7), '"
                    + value.AsInstant.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture)
                    + "', 127)",
                QueryexType.QxHierarchyId => "CAST('" + value.AsNode + "' AS hierarchyid)",
                QueryexType.QxGeography => "geography::STGeomFromText('"
                    + value.AsText
                    + "', 4326)",
                _ => "NULL",
            };
        }
    }
}
