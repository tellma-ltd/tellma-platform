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
    /// <param name="Restrictions">
    ///     The key-set restrictions, separated by semicolons. Each is a path and a table name, then
    ///     optionally an equals sign and the comma-separated keys the table holds when the query is
    ///     run: <c>Customer.Id @tb0_parties = 1, 2</c>.
    /// </param>
    /// <param name="UndeclaredColumns">The undeclared root columns to read, separated by commas.</param>
    /// <param name="Via">
    ///     A dotted navigation path the filter is rebased onto, so that its paths resolve from the
    ///     path's target: <c>Customer.Region</c>. With no filter, the rebase asks only that the path
    ///     reach a row.
    /// </param>
    /// <param name="Exists">
    ///     Semi-joins conjoined with the filter, separated by semicolons. Each is an entity and its
    ///     navigation to the root, then optionally a colon and the criterion a pointing row must
    ///     satisfy: <c>InvoiceLine Invoice: Amount &gt; 10</c>.
    /// </param>
    /// <param name="Parameters">
    ///     The declared parameters, separated by commas, each a name and a type:
    ///     <c>Customer numeric, AsOf date</c>. Every one may be absent, and binds no value when the
    ///     query is run.
    /// </param>
    /// <param name="Omitted">The declared parameters the compilation leaves out, separated by commas.</param>
    /// <param name="Constants">
    ///     The items of a constant statement, separated by semicolons, each a type and an expression
    ///     that reads no row: <c>date startOfMonth(today()); numeric me()</c>. When there are any,
    ///     the page compiles the statement that answers them in place of the query.
    /// </param>
    /// <param name="Conjuncts">
    ///     Whether to report the composed filter's top-level restrictions, as discovery reads them.
    /// </param>
    /// <param name="Measure">
    ///     A measure whose row condition to report, under the declared and omitted parameters.
    /// </param>
    public sealed record InspectRequest(
        string? Select,
        string? Filter,
        string? Having,
        string? OrderBy,
        bool Aggregate,
        string? Root,
        bool Execute,
        string? Restrictions = null,
        string? UndeclaredColumns = null,
        string? Via = null,
        string? Exists = null,
        string? Parameters = null,
        string? Omitted = null,
        string? Constants = null,
        bool Conjuncts = false,
        string? Measure = null);

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

    /// <summary>One top-level restriction of the filter, as the page shows it.</summary>
    /// <param name="Location">Which filter leaf it is in.</param>
    /// <param name="Start">Where the atom starts in that leaf.</param>
    /// <param name="Length">How long the atom is.</param>
    /// <param name="Compared">The compared path, dotted, or the compared expression's text.</param>
    /// <param name="Operator">How it is compared.</param>
    /// <param name="Value">The literal's value, or null for the absent value.</param>
    /// <param name="Type">The compared side's type.</param>
    public sealed record ConjunctView(
        string? Location,
        int Start,
        int Length,
        string Compared,
        string Operator,
        string? Value,
        string Type);

    /// <summary>Everything the page shows for one query, or for one constant statement.</summary>
    /// <param name="Tokens">
    ///     The scanned tokens of the select list, or of a constant statement's first item.
    /// </param>
    /// <param name="Canonical">The same text printed back in its shortest form.</param>
    /// <param name="Explicit">The same, with every grouping written out.</param>
    /// <param name="Nodes">The same text, bound.</param>
    /// <param name="Diagnostics">Whatever went wrong.</param>
    /// <param name="Sql">The compiled SQL, when it compiled.</param>
    /// <param name="Parameters">The parameters, when it compiled.</param>
    /// <param name="Columns">The result columns, when it compiled.</param>
    /// <param name="Rows">What a server answered, when one was asked.</param>
    /// <param name="Error">What went wrong outside the compiler, when something did.</param>
    /// <param name="Conjuncts">The filter's top-level restrictions, when they were asked for.</param>
    /// <param name="MeasureCondition">
    ///     The rows the page's measure aggregates, as a predicate; what kept it from being worked
    ///     out; or nothing when no measure was given.
    /// </param>
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
        string? Error,
        IReadOnlyList<ConjunctView>? Conjuncts = null,
        string? MeasureCondition = null);

    /// <summary>
    ///     Runs one query, or one constant statement, through every stage and reports what each of
    ///     them produced.
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

            // What the page declares and what it asks to have answered without a row, read from the
            // page's own syntax before anything else: both decide what the stage-by-stage panes show.
            List<QueryexParameterDeclaration> declared;
            List<ConstantItem> constants;
            try
            {
                declared = Declared(request.Parameters);
                constants = Constants(request.Constants);
            }
            catch (ArgumentException problem)
            {
                return new InspectResponse([], string.Empty, string.Empty, [], [], null, [], [], null, problem.Message);
            }

            InspectResponse response = await CompileAsync(request, connectionString, select, root, declared, constants);

            // A constant statement has no filter and no measure; a query has both readings beside
            // its compilation, whatever the compilation came to.
            return constants.Count > 0
                ? response
                : response with
                {
                    Conjuncts = request.Conjuncts ? Conjuncts(request, select, root) : null,
                    MeasureCondition = Measured(request.Measure, declared, Names(request.Omitted)),
                };
        }

        /// <summary>Compiles the page's query, or its constant statement, and runs it when asked.</summary>
        /// <param name="request">What the page asked about.</param>
        /// <param name="connectionString">A server to run against, when one was configured.</param>
        /// <param name="select">The select list, defaulted when the page left it empty.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <param name="declared">The declared parameters.</param>
        /// <param name="constants">The items of a constant statement; none for a query.</param>
        /// <returns>What each stage produced.</returns>
        private static async Task<InspectResponse> CompileAsync(
            InspectRequest request,
            string? connectionString,
            string select,
            EntityDescriptor root,
            List<QueryexParameterDeclaration> declared,
            List<ConstantItem> constants)
        {
            // The stage-by-stage panes follow the select list, or the first item of a constant
            // statement when the page asked for one.
            bool constant = constants.Count > 0;
            string shown = constant ? constants[0].Text : select;
            ProbeSyntax parsed = QueryexProbe.Parse(shown);
            List<TokenView> tokens = [.. parsed.Tokens.Select(
                token => new TokenView(token.Kind, token.Text, token.Start, token.Length))];

            List<NodeView> nodes = constant
                ? BoundWithoutARow(shown)
                : Bound(shown, root, request.Aggregate, declared);

            IReadOnlyList<(string Table, IReadOnlyList<string> Keys)> tables = [];
            QueryexResult<CompiledQuery> compiled;
            try
            {
                if (constant)
                {
                    // A statement of its own: the clauses and the host structure beside them are
                    // not part of it, and are left alone until the items are cleared.
                    compiled = Engine.EvaluateConstant(
                        constants,
                        new ConstantCompilationOptions
                        {
                            QueryexVersion = QueryexLanguage.Version,
                            Schema = LedgerFixture.Schema,
                        });
                }
                else
                {
                    // Host structure, read from the page's own syntax. A malformed entry is a caller
                    // error, and is shown the way the engine's own caller errors are.
                    (IReadOnlyList<KeySetRestriction> restrictions, tables) = Restrictions(request.Restrictions);
                    QuerySpec spec = new()
                    {
                        Root = root,
                        Select = select,
                        Aggregate = request.Aggregate,
                        Filter = Filter(request.Filter, request.Via, request.Exists),
                        Having = request.Aggregate ? Leaf(request.Having) : null,
                        OrderBy = string.IsNullOrWhiteSpace(request.OrderBy) ? null : request.OrderBy,
                        Restrictions = restrictions,
                        UndeclaredColumns = Names(request.UndeclaredColumns),
                    };

                    compiled = Engine.CompileQuery(
                        spec,
                        new QueryCompilationOptions
                        {
                            // The current version, because the page compiles what is being typed
                            // right now rather than something stored under a version of its own.
                            QueryexVersion = QueryexLanguage.Version,
                            Schema = LedgerFixture.Schema,
                            Parameters = declared,
                            OmittedParameters = Names(request.Omitted),
                        });
                }
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
                ? await RunAsync(compiled.Value, tables, connectionString)
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

        /// <summary>Reads the composed filter's top-level restrictions, as discovery does.</summary>
        /// <param name="request">What the page asked about.</param>
        /// <param name="select">The select list.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <returns>The restrictions; none when the page's host structure is not of its form.</returns>
        private static List<ConjunctView> Conjuncts(InspectRequest request, string select, EntityDescriptor root)
        {
            try
            {
                DiscoveryResult discovered = Engine.DiscoverQuery(
                    new QueryDiscoverySpec
                    {
                        Select = select,
                        Aggregate = request.Aggregate,
                        Filter = Filter(request.Filter, request.Via, request.Exists),
                        Having = request.Aggregate ? Leaf(request.Having) : null,
                        OrderBy = string.IsNullOrWhiteSpace(request.OrderBy) ? null : request.OrderBy,
                        Conjuncts = true,
                    },
                    new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = root });

                return [.. (discovered.Conjuncts ?? []).Select(conjunct => new ConjunctView(
                    conjunct.Site.Location,
                    conjunct.Site.Span.Start,
                    conjunct.Site.Span.Length,
                    conjunct.Path is null ? conjunct.Expression ?? string.Empty : string.Join('.', conjunct.Path),
                    conjunct.Operator.ToString(),
                    conjunct.Value is IFormattable formattable
                        ? formattable.ToString(null, CultureInfo.InvariantCulture)
                        : conjunct.Value?.ToString(),
                    conjunct.Type.ToString()))];
            }
            catch (ArgumentException)
            {
                // The compilation beside it reports the same mistake in the page's host structure.
                return [];
            }
        }

        /// <summary>Works out the rows a measure aggregates, under the page's parameters.</summary>
        /// <param name="measure">The measure, or nothing.</param>
        /// <param name="declared">The declared parameters.</param>
        /// <param name="omitted">The declared parameters left out.</param>
        /// <returns>The condition, what kept it from being worked out, or null without a measure.</returns>
        /// <remarks>
        ///     The page gives no parameter a value, so a parameter that is not left out stays in the
        ///     condition as written.
        /// </remarks>
        private static string? Measured(string? measure, List<QueryexParameterDeclaration> declared, string[] omitted)
        {
            if (string.IsNullOrWhiteSpace(measure))
            {
                return null;
            }

            try
            {
                QueryexResult<MeasureRows> result = Engine.MeasureCondition(
                    measure,
                    new ParameterBindingOptions
                    {
                        Mode = QueryexMode.Aggregate,
                        QueryexVersion = QueryexLanguage.Version,
                        Parameters = declared,
                        Omitted = omitted,
                    });

                return result.Succeeded
                    ? result.Value.Condition ?? "(every row)"
                    : string.Join(", ", result.Diagnostics.Select(d => d.Code + " @" + d.Span.Start.ToString(CultureInfo.InvariantCulture)));
            }
            catch (ArgumentException problem)
            {
                return problem.Message;
            }
        }

        /// <summary>Turns a piece of predicate text into a filter, when there is any.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The filter, or null.</returns>
        private static FilterTree? Leaf(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : FilterTree.Leaf(text);
        }

        /// <summary>Composes the filter from the page's filter, rebase and semi-joins.</summary>
        /// <param name="filter">The filter text.</param>
        /// <param name="via">The navigation path the filter is rebased onto, when there is one.</param>
        /// <param name="exists">The semi-joins, separated by semicolons.</param>
        /// <returns>The composed filter, or null when the page supplied none of the three.</returns>
        /// <exception cref="ArgumentException">An entry is not of the page's form.</exception>
        private static FilterTree? Filter(string? filter, string? via, string? exists)
        {
            List<FilterTree> conjuncts = [];
            FilterTree? leaf = Leaf(filter);
            if (!string.IsNullOrWhiteSpace(via))
            {
                conjuncts.Add(FilterTree.Via(via.Trim(), leaf ?? FilterTree.And([])));
            }
            else if (leaf is not null)
            {
                conjuncts.Add(leaf);
            }

            foreach (string entry in Split(exists, ';'))
            {
                // "<entity> <navigation>", then optionally ": <criterion>".
                int colon = entry.IndexOf(':', StringComparison.Ordinal);
                string head = colon < 0 ? entry : entry[..colon];
                string[] parts = head.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new ArgumentException(
                        "A semi-join is written as an entity and its navigation, such as 'InvoiceLine Invoice: Amount > 10'.",
                        nameof(exists));
                }

                FilterTree? criterion = colon < 0 ? null : Leaf(entry[(colon + 1)..]);
                conjuncts.Add(FilterTree.Exists(parts[0], parts[1], criterion ?? FilterTree.And([])));
            }

            return conjuncts.Count switch
            {
                0 => null,
                1 => conjuncts[0],
                _ => FilterTree.And(conjuncts),
            };
        }

        /// <summary>Reads the page's restrictions.</summary>
        /// <param name="text">The restrictions, separated by semicolons.</param>
        /// <returns>The restrictions, and the keys each table holds when the query is run.</returns>
        /// <exception cref="ArgumentException">An entry is not a path and a table name.</exception>
        private static (IReadOnlyList<KeySetRestriction> Restrictions, IReadOnlyList<(string Table, IReadOnlyList<string> Keys)> Tables)
            Restrictions(string? text)
        {
            List<KeySetRestriction> restrictions = [];
            List<(string, IReadOnlyList<string>)> tables = [];
            foreach (string entry in Split(text, ';'))
            {
                // "<path> <table>", then optionally "= <key>, <key>, …".
                int equals = entry.IndexOf('=', StringComparison.Ordinal);
                string head = equals < 0 ? entry : entry[..equals];
                string[] parts = head.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new ArgumentException(
                        "A restriction is written as a path and a table name, such as 'Customer.Id @tb0_parties = 1, 2'.",
                        nameof(text));
                }

                restrictions.Add(new KeySetRestriction(parts[0], parts[1]));
                tables.Add((parts[1], equals < 0 ? [] : Split(entry[(equals + 1)..], ',')));
            }

            return (restrictions, tables);
        }

        /// <summary>Reads the page's declared parameters.</summary>
        /// <param name="text">The declarations, separated by commas.</param>
        /// <returns>The declarations, each of a value that may be absent.</returns>
        /// <exception cref="ArgumentException">An entry is not a name and a type.</exception>
        private static List<QueryexParameterDeclaration> Declared(string? text)
        {
            List<QueryexParameterDeclaration> declared = [];
            foreach (string entry in Split(text, ','))
            {
                // "<name> <type>", the name with or without the sign it is referred to by.
                string[] parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    throw new ArgumentException(
                        "A parameter is declared as a name and a type, such as 'Customer numeric'.",
                        nameof(text));
                }

                declared.Add(new QueryexParameterDeclaration(
                    parts[0].TrimStart('@'),
                    TypeNamed(parts[1]),
                    IsNotNull: false));
            }

            return declared;
        }

        /// <summary>Reads the items of the page's constant statement.</summary>
        /// <param name="text">The items, separated by semicolons.</param>
        /// <returns>The items; none when the page asks about a query.</returns>
        /// <exception cref="ArgumentException">An entry is not a type and an expression.</exception>
        private static List<ConstantItem> Constants(string? text)
        {
            List<ConstantItem> items = [];
            foreach (string entry in Split(text, ';'))
            {
                // "<type> <expression>": the first word is the type, and everything after it is the
                // expression as typed.
                int space = entry.IndexOf(' ', StringComparison.Ordinal);
                if (space < 0)
                {
                    throw new ArgumentException(
                        "A constant item is written as a type and an expression, such as 'date startOfMonth(today())'.",
                        nameof(text));
                }

                items.Add(new ConstantItem(entry[(space + 1)..].Trim(), TypeNamed(entry[..space])));
            }

            return items;
        }

        /// <summary>Resolves a type by the name the language's conversions know it by.</summary>
        /// <param name="name">The name, in any case: <c>numeric</c>, <c>date</c>, <c>string</c>.</param>
        /// <returns>The type.</returns>
        /// <exception cref="ArgumentException">No type goes by that name.</exception>
        private static QueryexType TypeNamed(string name)
        {
            // The public names carry a prefix the page has no reason to make anybody type. Matched
            // against the names themselves, so a number is never read as a member's value.
            foreach (QueryexType type in Enum.GetValues<QueryexType>())
            {
                if (string.Equals("Qx" + name, type.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return type;
                }
            }

            throw new ArgumentException(
                "No type is named '" + name + "'; the types are "
                    + string.Join(", ", Enum.GetNames<QueryexType>().Select(member => member[2..].ToLowerInvariant()))
                    + ".",
                nameof(name));
        }

        /// <summary>Reads a comma-separated list of names.</summary>
        /// <param name="text">The names.</param>
        /// <returns>The names, trimmed.</returns>
        private static string[] Names(string? text)
        {
            return Split(text, ',');
        }

        /// <summary>Splits text on a separator, dropping empty entries and surrounding blanks.</summary>
        /// <param name="text">The text, or null.</param>
        /// <param name="separator">The separator.</param>
        /// <returns>The entries.</returns>
        private static string[] Split(string? text, char separator)
        {
            return string.IsNullOrWhiteSpace(text)
                ? []
                : text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>Binds the select list and flattens what came out.</summary>
        /// <param name="select">The select list.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <param name="aggregate">Whether the query groups.</param>
        /// <param name="declared">The declared parameters.</param>
        /// <returns>The bound nodes.</returns>
        private static List<NodeView> Bound(
            string select,
            EntityDescriptor root,
            bool aggregate,
            IReadOnlyList<QueryexParameterDeclaration> declared)
        {
            return Flattened(QueryexProbe.Bind(select, new ProbeBindOptions
            {
                Schema = LedgerFixture.Schema,
                Root = root.Name,
                Mode = aggregate ? QueryexMode.Aggregate : QueryexMode.Value,
                HasGroupingKeys = aggregate,
                Parameters = declared,
            }));
        }

        /// <summary>Binds one item of a constant statement and flattens what came out.</summary>
        /// <param name="item">The item's text.</param>
        /// <returns>The bound nodes.</returns>
        private static List<NodeView> BoundWithoutARow(string item)
        {
            // No entity goes by the empty name, so the item binds with no row to read a path from,
            // which is how the statement itself binds it.
            return Flattened(QueryexProbe.Bind(item, new ProbeBindOptions
            {
                Schema = LedgerFixture.Schema,
                Root = string.Empty,
                Mode = QueryexMode.Value,
            }));
        }

        /// <summary>Flattens a bound list into the rows the page shows.</summary>
        /// <param name="bound">The bound list.</param>
        /// <returns>The bound nodes.</returns>
        private static List<NodeView> Flattened(ProbeBinding bound)
        {
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
        /// <param name="tables">The tables of keys the restrictions read, with the keys each holds.</param>
        /// <param name="connectionString">The server.</param>
        /// <returns>The rows, or what went wrong.</returns>
        /// <remarks>
        ///     Without a connection string the tool opens no connection at all; running against a
        ///     server is something a developer turns on deliberately.
        /// </remarks>
        private static async Task<(IReadOnlyList<IReadOnlyList<string>>?, string?)> RunAsync(
            CompiledQuery query,
            IReadOnlyList<(string Table, IReadOnlyList<string> Keys)> tables,
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
                command.CommandText = Declarations(tables) + query.Sql;
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

        /// <summary>Declares and fills the tables of keys the restrictions read.</summary>
        /// <param name="tables">The tables, with the keys each holds.</param>
        /// <returns>The statements to run ahead of the query.</returns>
        /// <remarks>
        ///     Each table holds its keys as text, which the backend converts to the type of whatever
        ///     column it is compared with, so a playground need not know the key's type. The text
        ///     carries the fixture's collation so a text key compares without a collation conflict.
        ///     Keys are written as literals with their quotes doubled, because this is a developer's
        ///     own input to a developer's own database.
        /// </remarks>
        private static string Declarations(IReadOnlyList<(string Table, IReadOnlyList<string> Keys)> tables)
        {
            System.Text.StringBuilder script = new();

            // One table per name, ignoring case as the backend does, holding every key any entry
            // naming it listed: two restrictions read the same table, so the keys written beside
            // either belong to it whichever entry came first.
            IEnumerable<(string Table, string[] Keys)> merged = tables
                .GroupBy(entry => entry.Table, StringComparer.OrdinalIgnoreCase)
                .Select(group => (group.First().Table, group.SelectMany(entry => entry.Keys).Distinct(StringComparer.Ordinal).ToArray()));

            foreach ((string table, string[] keys) in merged)
            {
                script.Append("DECLARE ").Append(table).Append(" TABLE ([Id] nvarchar(4000) COLLATE ")
                    .Append(LedgerDdl.Collation).Append(");\n");

                if (keys.Length > 0)
                {
                    script.Append("INSERT INTO ").Append(table).Append(" ([Id]) VALUES ")
                        .Append(string.Join(", ", keys.Select(key => "(N'" + key.Replace("'", "''", StringComparison.Ordinal) + "')")))
                        .Append(";\n");
                }
            }

            return script.ToString();
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
