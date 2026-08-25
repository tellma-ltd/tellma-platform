// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Queryex.Inspection
{
    /// <summary>What the page asks about.</summary>
    /// <param name="Select">The select list.</param>
    /// <param name="Filter">The row-level predicate, as one expression.</param>
    /// <param name="Having">The group-level predicate, as one expression.</param>
    /// <param name="OrderBy">The ordering list.</param>
    /// <param name="Aggregate">Whether the query groups.</param>
    /// <param name="Root">The entity paths resolve from.</param>
    /// <param name="Execute">Whether to run the compiled query against a server.</param>
    public sealed record InspectRequest(
        string? Select,
        string? Filter,
        string? Having,
        string? OrderBy,
        bool Aggregate,
        string? Root,
        bool Execute);

    /// <summary>One scanned token, as the page shows it.</summary>
    /// <param name="Kind">What kind of token it is.</param>
    /// <param name="Text">Its text.</param>
    /// <param name="Start">Where it starts.</param>
    /// <param name="Length">How long it is.</param>
    public sealed record TokenView(string Kind, string? Text, int Start, int Length);

    /// <summary>One bound node, as the page shows it.</summary>
    /// <param name="Depth">How deeply it is nested.</param>
    /// <param name="Kind">What kind of node it is.</param>
    /// <param name="Type">Its type.</param>
    /// <param name="Nullity">Whether it can be absent.</param>
    /// <param name="Detail">Whatever else is worth showing about it.</param>
    public sealed record NodeView(int Depth, string Kind, string Type, string Nullity, string? Detail);

    /// <summary>One problem, as the page shows it.</summary>
    /// <param name="Code">Its stable code.</param>
    /// <param name="Location">Which input it is in.</param>
    /// <param name="Start">Where it starts.</param>
    /// <param name="Length">How long the offending range is.</param>
    public sealed record ProblemView(string Code, string? Location, int Start, int Length);

    /// <summary>One parameter of the compiled query, as the page shows it.</summary>
    /// <param name="Name">Its name in the SQL.</param>
    /// <param name="Type">Its type in the language.</param>
    /// <param name="StoreType">The type it binds as.</param>
    /// <param name="Origin">Where its value comes from.</param>
    /// <param name="Value">Its value, when one is fixed at compile time.</param>
    public sealed record SlotView(string Name, string Type, string StoreType, string Origin, string? Value);

    /// <summary>One result column, as the page shows it.</summary>
    /// <param name="Ordinal">Its position.</param>
    /// <param name="Text">The select item it came from.</param>
    /// <param name="Type">Its type.</param>
    /// <param name="Nullity">Whether it can be absent.</param>
    /// <param name="IsGroupingKey">Whether it is one of the grouping keys.</param>
    public sealed record ColumnView(int Ordinal, string Text, string Type, string Nullity, bool IsGroupingKey);

    /// <summary>Everything the page shows for one query.</summary>
    /// <param name="Tokens">The scanned tokens of the select list.</param>
    /// <param name="Canonical">The select list printed back in its shortest form.</param>
    /// <param name="Explicit">The same, with every grouping written out.</param>
    /// <param name="Nodes">The bound select list.</param>
    /// <param name="Diagnostics">Whatever went wrong.</param>
    /// <param name="Sql">The compiled SQL, when it compiled.</param>
    /// <param name="Parameters">The parameters, when it compiled.</param>
    /// <param name="Columns">The result columns, when it compiled.</param>
    /// <param name="Rows">What a server answered, when one was asked.</param>
    /// <param name="Error">What went wrong outside the compiler, when something did.</param>
    public sealed record InspectResponse(
        IReadOnlyList<TokenView> Tokens,
        string Canonical,
        string Explicit,
        IReadOnlyList<NodeView> Nodes,
        IReadOnlyList<ProblemView> Diagnostics,
        string? Sql,
        IReadOnlyList<SlotView> Parameters,
        IReadOnlyList<ColumnView> Columns,
        IReadOnlyList<IReadOnlyList<string>>? Rows,
        string? Error);

    /// <summary>
    ///     Runs one query through every stage and reports what each of them produced.
    /// </summary>
    /// <remarks>
    ///     Reads the same fixture schema and the same stage-by-stage view the conformance suites do.
    ///     What somebody eyeballs here is therefore the very thing the suites pin, rather than a
    ///     second rendering of it that could quietly disagree.
    /// </remarks>
    public static class Inspection
    {
        /// <summary>The engine the playground compiles with.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Runs one query through every stage.</summary>
        /// <param name="request">What the page asked about.</param>
        /// <param name="connectionString">A server to run against, when one was configured.</param>
        /// <returns>What each stage produced.</returns>
        public static async Task<InspectResponse> InspectAsync(
            InspectRequest request,
            string? connectionString)
        {
            ArgumentNullException.ThrowIfNull(request);

            string select = string.IsNullOrWhiteSpace(request.Select) ? "Id" : request.Select;
            EntityDescriptor root = LedgerFixture.Schema.FindEntity(request.Root ?? "Invoice")
                ?? LedgerFixture.Invoice;

            ProbeSyntax parsed = QueryexProbe.Parse(select);
            List<TokenView> tokens = [.. parsed.Tokens.Select(
                token => new TokenView(token.Kind, token.Text, token.Start, token.Length))];

            QuerySpec spec = new()
            {
                Root = root,
                Select = select,
                Aggregate = request.Aggregate,
                Filter = Leaf(request.Filter),
                Having = request.Aggregate ? Leaf(request.Having) : null,
                OrderBy = string.IsNullOrWhiteSpace(request.OrderBy) ? null : request.OrderBy,
            };

            List<NodeView> nodes = Bound(select, root, request.Aggregate);

            QueryexResult<CompiledQuery> compiled;
            try
            {
                compiled = Engine.CompileQuery(
                    spec,
                    new QueryCompilationOptions
                    {
                        // The current version, because the page compiles what is being typed right
                        // now rather than something stored under a version of its own.
                        LanguageVersion = QueryexLanguage.Version,
                        Schema = LedgerFixture.Schema,
                    });
            }
            catch (ArgumentException problem)
            {
                // Only a mistake in what the page itself sent reaches here; anything a user typed
                // comes back as a diagnostic.
                return new InspectResponse(
                    tokens,
                    parsed.CanonicalText,
                    parsed.ExplicitText,
                    nodes,
                    [],
                    null,
                    [],
                    [],
                    null,
                    problem.Message);
            }

            if (!compiled.Succeeded)
            {
                return new InspectResponse(
                    tokens,
                    parsed.CanonicalText,
                    parsed.ExplicitText,
                    nodes,
                    [.. compiled.Diagnostics.Select(Problem)],
                    null,
                    [],
                    [],
                    null,
                    null);
            }

            (IReadOnlyList<IReadOnlyList<string>>? rows, string? error) = request.Execute
                ? await RunAsync(compiled.Value, connectionString)
                : (null, null);

            return new InspectResponse(
                tokens,
                parsed.CanonicalText,
                parsed.ExplicitText,
                nodes,
                [],
                compiled.Value.Sql,
                [.. compiled.Value.Parameters.Select(Slot)],
                [.. compiled.Value.Columns.Select(Column)],
                rows,
                error);
        }

        /// <summary>Turns a piece of predicate text into a filter, when there is any.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The filter, or null.</returns>
        private static FilterTree? Leaf(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : FilterTree.Leaf(text);
        }

        /// <summary>Binds the select list and flattens what came out.</summary>
        /// <param name="select">The select list.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <param name="aggregate">Whether the query groups.</param>
        /// <returns>The bound nodes.</returns>
        private static List<NodeView> Bound(string select, EntityDescriptor root, bool aggregate)
        {
            ProbeBinding bound = QueryexProbe.Bind(select, new ProbeBindOptions
            {
                Schema = LedgerFixture.Schema,
                Root = root.Name,
                Mode = aggregate ? QueryexMode.Aggregate : QueryexMode.Value,
                HasGroupingKeys = aggregate,
            });

            List<NodeView> nodes = [];
            foreach (ProbeBoundNode node in bound.Nodes)
            {
                nodes.Add(new NodeView(
                    Depth(bound.Nodes, node),
                    node.Kind,
                    node.Type,
                    node.Nullity,
                    node.Path is not null ? string.Join('.', node.Path) : node.Detail));
            }

            return nodes;
        }

        /// <summary>How deeply one bound node is nested.</summary>
        /// <param name="nodes">Every node, flattened.</param>
        /// <param name="node">The node.</param>
        /// <returns>Its depth, counting the root as zero.</returns>
        private static int Depth(IReadOnlyList<ProbeBoundNode> nodes, ProbeBoundNode node)
        {
            int depth = 0;
            int parent = node.Parent;
            while (parent >= 0 && parent < nodes.Count && depth < nodes.Count)
            {
                depth++;
                parent = nodes[parent].Parent;
            }

            return depth;
        }

        /// <summary>Renders one diagnostic.</summary>
        /// <param name="diagnostic">The diagnostic.</param>
        /// <returns>The rendered form.</returns>
        private static ProblemView Problem(QueryexDiagnostic diagnostic)
        {
            return new ProblemView(
                diagnostic.Code,
                diagnostic.Location,
                diagnostic.Span.Start,
                diagnostic.Span.Length);
        }

        /// <summary>Renders one parameter.</summary>
        /// <param name="slot">The slot.</param>
        /// <returns>The rendered form.</returns>
        private static SlotView Slot(QueryexParameterSlot slot)
        {
            return new SlotView(
                slot.Name,
                slot.Type.ToString(),
                slot.StoreType.Family + (slot.StoreType.Size is int size
                    ? "(" + size.ToString(CultureInfo.InvariantCulture) + ")"
                    : string.Empty),
                slot.Origin.ToString(),
                slot.Value is null
                    ? slot.DeclaredName
                    : Convert.ToString(slot.Value, CultureInfo.InvariantCulture));
        }

        /// <summary>Renders one result column.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The rendered form.</returns>
        private static ColumnView Column(QueryexColumn column)
        {
            return new ColumnView(
                column.Ordinal,
                column.Text,
                column.Type.ToString(),
                column.Nullity.ToString(),
                column.IsGroupingKey);
        }

        /// <summary>Runs a compiled query against a server, when one was configured.</summary>
        /// <param name="query">The compiled query.</param>
        /// <param name="connectionString">The server.</param>
        /// <returns>The rows, or what went wrong.</returns>
        /// <remarks>
        ///     Without a connection string the tool opens no connection at all; running against a
        ///     server is something a developer turns on deliberately.
        /// </remarks>
        private static async Task<(IReadOnlyList<IReadOnlyList<string>>?, string?)> RunAsync(
            CompiledQuery query,
            string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return (null, "No server is configured; pass one with --connection.");
            }

            try
            {
                await using SqlConnection connection = new(connectionString);
                await connection.OpenAsync();
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = query.Sql;
                Bind(command, query);

                List<IReadOnlyList<string>> rows = [];
                await using SqlDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    List<string> values = [];
                    for (int index = 0; index < reader.FieldCount; index++)
                    {
                        values.Add(reader.IsDBNull(index)
                            ? string.Empty
                            : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)
                                ?? string.Empty);
                    }

                    rows.Add(values);
                }

                return (rows, null);
            }
            catch (SqlException problem)
            {
                return (null, problem.Message);
            }
        }

        /// <summary>Binds every slot with a value a playground can supply.</summary>
        /// <param name="command">The command.</param>
        /// <param name="query">The compiled query.</param>
        private static void Bind(SqlCommand command, CompiledQuery query)
        {
            InterpreterContext context = InterpreterContext.Fixed;
            foreach (QueryexParameterSlot slot in query.Parameters)
            {
                object? value = slot.Origin switch
                {
                    QueryexParameterOrigin.Today => context.Today.ToDateTime(TimeOnly.MinValue),
                    QueryexParameterOrigin.Now => context.Now,
                    QueryexParameterOrigin.UserId => context.UserId,
                    QueryexParameterOrigin.TimeZone => "E. Africa Standard Time",
                    QueryexParameterOrigin.Declared => null,
                    QueryexParameterOrigin.Literal => slot.Value is System.Data.SqlTypes.SqlDecimal number
                        ? number.Value
                        : slot.Value,
                    _ => slot.Value,
                };

                command.Parameters.AddWithValue(slot.Name, value ?? DBNull.Value);
            }
        }
    }
}
