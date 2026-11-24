// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Composition;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Query
{
    // The server's half of the findable golden (spec 0029 §17 and §18): the client's cached search
    // and paste resolver read the same rows, so a change to either side's rule fails a suite. The
    // paste-leaf golden holds the fullest filter leaves the client's paste resolver writes, which
    // the server must compile under the query pipeline's limits (§14.5).

    /// <summary>
    ///     The search lowering against the findable and search-text rows of the display-name golden
    ///     the client reads too: for each findable row, the rows the lowered search's findable
    ///     branches match (the id, an integer source or prefix, and the prefix-and-name split) are
    ///     the golden's search ids; for each search-text row, the rows its text columns' matches
    ///     find, each column from its start or anywhere by its kind, are the golden's found ids. And
    ///     the paste-leaf golden's leaves, which the client writes, compile under the query
    ///     pipeline's limits.
    /// </summary>
    public sealed partial class FindableGoldenTests : IAsyncDisposable
    {
        /// <summary>The golden file's reading options: camel-case members, enums by name.</summary>
        private static readonly JsonSerializerOptions GoldenJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        /// <summary>The golden file, read once.</summary>
        private static readonly Lazy<Golden> File = new(() => Load<Golden>("display-name-golden.json"));

        /// <summary>The paste-leaf golden file, read once.</summary>
        private static readonly Lazy<PasteGolden> PasteFile = new(() => Load<PasteGolden>("paste-leaf-golden.json"));

        /// <summary>The findable model.</summary>
        private readonly QueryRig _rig = new(FindableModel.Compose);

        /// <summary>The names of the golden's findable rows.</summary>
        public static TheoryData<string> Rows { get; } = [.. File.Value.Findable.Select(static row => row.Name)];

        /// <summary>The names of the golden's search-text rows.</summary>
        public static TheoryData<string> TextRows { get; } = [.. File.Value.SearchText.Select(static row => row.Name)];

        /// <summary>The names of the paste-leaf golden's leaves.</summary>
        public static TheoryData<string> PasteLeaves { get; } = [.. PasteFile.Value.Leaves.Select(static leaf => leaf.Name)];

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        [Theory]
        [MemberData(nameof(Rows))]
        public void A_golden_rows_text_finds_its_search_ids_through_the_findable_branches(string name)
        {
            FindableRow row = File.Value.Findable.Single(candidate => candidate.Name == name);
            string resource = FindableModel.For(row.Rule);
            _rig.Shape = row.Shape;

            // The model's display name is the golden rule's.
            DisplayNameMetadata display = _rig.Surfaces.Stack(resource)!.DisplayName;
            Assert.Equal(row.Rule.Source ?? row.Rule.Twins![0], display.Source.Name);
            Assert.Equal(row.Rule.Prefix, display.Prefix?.Name);

            SearchFilter search = _rig.Search(resource, row.Text);
            var parameters = search.Parameters.ToDictionary(static parameter => parameter.Declaration.Name, static parameter => parameter.Value, StringComparer.Ordinal);
            List<Func<IReadOnlyDictionary<string, JsonElement>, bool>> branches =
                [.. Leaves(search.Tree).Where(static text => !ReadsSearch().IsMatch(text)).Select(text => Branch(text, parameters))];

            int[] found = [.. row.Rows.Where(candidate => branches.Any(branch => branch(candidate))).Select(static candidate => Cell(candidate, "Id").GetInt32())];
            Assert.Equal(row.Search, found);
        }

        [Theory]
        [MemberData(nameof(TextRows))]
        public void A_golden_rows_text_finds_its_ids_through_the_text_columns_each_by_its_kind(string name)
        {
            SearchTextRow row = File.Value.SearchText.Single(candidate => candidate.Name == name);
            string resource = FindableModel.For(row.Rule, row.Searchable);
            _rig.Shape = row.Shape;

            // The model searches the golden's columns, each by the golden's kind.
            static string[] Kinds(IEnumerable<(string Property, SearchKind Kind)> columns)
            {
                return [.. columns.Select(static column => $"{column.Property}:{column.Kind}").Order(StringComparer.Ordinal)];
            }

            Assert.Equal(Kinds(row.Searchable.Select(static entry => (entry.Key, entry.Value))), Kinds(_rig.Surfaces.Stack(resource)!.Searchable));

            SearchFilter search = _rig.Search(resource, row.Text);
            string text = (string)search.Parameters.Single(static parameter => parameter.Declaration.Name == QuerySearch.SearchParameter).Value!;
            List<Func<IReadOnlyDictionary<string, JsonElement>, bool>> branches =
                [.. Leaves(search.Tree).Where(static leaf => ReadsSearch().IsMatch(leaf)).Select(leaf => TextBranch(leaf, text))];

            int[] found = [.. row.Rows.Where(candidate => branches.Any(branch => branch(candidate))).Select(static candidate => Cell(candidate, "Id").GetInt32())];
            Assert.Equal(row.Found, found);
        }

        [Fact]
        public void The_golden_holds_a_search_text_row_of_either_kind()
        {
            IReadOnlyList<SearchTextRow> rows = File.Value.SearchText;

            Assert.Contains(rows, static row => row.Searchable.Values.Contains(SearchKind.Prefix) && row.Found.Count > 0);
            Assert.Contains(rows, static row => row.Searchable.Values.Contains(SearchKind.Prefix) && row.Found.Count == 0);
            Assert.Contains(rows, static row => !row.Searchable.Values.Contains(SearchKind.Prefix));
        }

        [Theory]
        [MemberData(nameof(PasteLeaves))]
        public void A_paste_leaf_the_client_writes_compiles_under_the_pipeline_limits(string name)
        {
            PasteLeaf leaf = PasteFile.Value.Leaves.Single(candidate => candidate.Name == name);
            string resource = FindableModel.For(leaf.Rule);
            _rig.Shape = leaf.Shape;

            QuerySpec spec = new() { Root = _rig.Schema.FindEntity(resource)!, Select = "Id", Filter = FilterTree.Leaf(leaf.Leaf) };
            QueryexResult<CompiledQuery> compiled = _rig.Services.GetRequiredService<QueryexEngine>().CompileQuery(
                spec,
                new QueryCompilationOptions { Schema = _rig.Schema, QueryexVersion = QueryexLanguage.Version, Limits = QueryexProfiles.PipelineLimits });

            Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(static diagnostic => diagnostic.Code + "@" + diagnostic.Location)));
            // Each literal takes a slot, the same value of the same type sharing one: at most the leaf's literals.
            Assert.InRange(compiled.Value!.Parameters.Count, 1, leaf.Literals);
        }

        [Fact]
        public void The_paste_leaf_golden_holds_a_leaf_at_the_literals_ceiling_of_every_language_shape()
        {
            IReadOnlyList<PasteLeaf> leaves = PasteFile.Value.Leaves;

            Assert.All(Enum.GetValues<MultilingualShape>(), shape => Assert.Contains(leaves, leaf => leaf.Shape == shape && leaf.Literals >= (QueryexProfiles.PipelineLimits.MaxParameters / 2) - 2));
            Assert.Contains(leaves, static leaf => leaf.Rule.PrefixType == "integer");
            Assert.Contains(leaves, static leaf => leaf.Rule.SourceType == "integer");
            Assert.Contains(leaves, static leaf => leaf.Leaf.Length > QueryexLimits.Default.MaxInputLength - 256);
        }

        [Fact]
        public void The_golden_holds_a_row_of_every_findable_branch()
        {
            IReadOnlyList<FindableRow> rows = File.Value.Findable;

            Assert.Contains(rows, static row => row.Text.StartsWith('#'));
            Assert.Contains(rows, static row => row.Rule.SourceType == "integer");
            Assert.Contains(rows, static row => row.Rule.PrefixType == "integer");
            Assert.Contains(rows, static row => row.Rule.Prefix is not null && row.Text.Contains(DisplayNameMetadata.Separator, StringComparison.Ordinal));
        }

        /// <summary>Reads a golden file copied beside the suite.</summary>
        /// <typeparam name="T">The golden's type.</typeparam>
        /// <param name="file">The file's name.</param>
        /// <returns>The golden.</returns>
        private static T Load<T>(string file)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Queries", file);
            return JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(path), GoldenJson)
                ?? throw new InvalidOperationException($"The golden {file} is empty.");
        }

        /// <summary>A lowered search's disjuncts, as written.</summary>
        /// <param name="tree">The search's tree: a disjunction, or <c>false</c> when no branch applies.</param>
        /// <returns>The disjuncts' texts.</returns>
        private static IEnumerable<string> Leaves(FilterTree tree)
        {
            return tree switch
            {
                FilterTree.OrNode or => or.Children.Select(static child => ((FilterTree.LeafNode)child).Text),
                FilterTree.LeafNode { Text: "false" } => [],
                _ => throw new InvalidOperationException($"A search lowered to {QueryRig.Render(tree)}, which is no disjunction."),
            };
        }

        /// <summary>
        ///     A findable branch as a row predicate: a column equal to the integer the text reads, or
        ///     the split's prefix equal to its left side and a slot starting with its right one. Text
        ///     compares ignoring case, as the database's collation does for these rows.
        /// </summary>
        /// <param name="text">The branch's text.</param>
        /// <param name="parameters">The search's parameters, by name.</param>
        /// <returns>The predicate.</returns>
        private static Func<IReadOnlyDictionary<string, JsonElement>, bool> Branch(string text, Dictionary<string, object?> parameters)
        {
            // --- A column equal to the integer: the id, an integer source or prefix ---
            if (IdBranch().Match(text) is { Success: true } id)
            {
                decimal value = (decimal)parameters[QuerySearch.IdParameter]!;
                string column = id.Groups["column"].Value;
                return row => Cell(row, column) is { ValueKind: JsonValueKind.Number } cell && cell.GetDecimal() == value;
            }

            // --- The split: the prefix equal to the left side, a slot starting with the right one ---
            if (SplitBranch().Match(text) is { Success: true } split)
            {
                object left = parameters[QuerySearch.LeftParameter]!;
                string right = (string)parameters[QuerySearch.RightParameter]!;
                string prefix = split.Groups["prefix"].Value;
                string[] slots = [.. SplitSlot().Matches(split.Groups["slots"].Value).Select(static match => match.Groups["slot"].Value)];
                return row => Equal(Cell(row, prefix), left)
                    && slots.Any(slot => Cell(row, slot) is { ValueKind: JsonValueKind.String } cell && cell.GetString()!.StartsWith(right, StringComparison.OrdinalIgnoreCase));
            }

            throw new InvalidOperationException($"The search lowered a branch the golden does not cover: {text}");
        }

        /// <summary>
        ///     A text column's branch as a row predicate: the column starting with the search text, or
        ///     holding it anywhere. Text compares ignoring case, as the database's collation does for
        ///     these rows.
        /// </summary>
        /// <param name="branch">The branch's text.</param>
        /// <param name="text">The search text, as its parameter binds it.</param>
        /// <returns>The predicate.</returns>
        private static Func<IReadOnlyDictionary<string, JsonElement>, bool> TextBranch(string branch, string text)
        {
            Match match = TextMatch().Match(branch);
            Assert.True(match.Success, $"The search lowered a text branch the golden does not cover: {branch}");
            string column = match.Groups["column"].Value;
            bool prefix = match.Groups["function"].Value == "startsWith";
            return row => Cell(row, column) is { ValueKind: JsonValueKind.String } cell
                && (prefix ? cell.GetString()!.StartsWith(text, StringComparison.OrdinalIgnoreCase) : cell.GetString()!.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether a cell holds the split's left side: the text ignoring case, or the number.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="left">The left side as its parameter binds it.</param>
        /// <returns>True when equal.</returns>
        private static bool Equal(JsonElement cell, object left)
        {
            return left switch
            {
                string text => cell.ValueKind == JsonValueKind.String && string.Equals(cell.GetString(), text, StringComparison.OrdinalIgnoreCase),
                decimal number => cell.ValueKind == JsonValueKind.Number && cell.GetDecimal() == number,
                _ => false,
            };
        }

        /// <summary>A golden row's cell by its column's name, the row's members being camel-case.</summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell; undefined when the row has none.</returns>
        private static JsonElement Cell(IReadOnlyDictionary<string, JsonElement> row, string column)
        {
            return row.FirstOrDefault(entry => string.Equals(entry.Key, column, StringComparison.OrdinalIgnoreCase)).Value;
        }

        /// <summary>A branch that reads the search text: a searchable or display-name column's match, outside the findable rule.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"@search\b", RegexOptions.CultureInvariant)]
        private static partial Regex ReadsSearch();

        /// <summary>A text column's match of the search text: from its start, or anywhere.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^(?<function>startsWith|contains)\((?<column>\w+), @search\)$", RegexOptions.CultureInvariant)]
        private static partial Regex TextMatch();

        /// <summary>A column equal to the integer the text reads.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^(?<column>\w+) = @searchId$", RegexOptions.CultureInvariant)]
        private static partial Regex IdBranch();

        /// <summary>The split: the prefix equal to the left side, and the slots' matches.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^(?<prefix>\w+) = @left and \((?<slots>.+)\)$", RegexOptions.CultureInvariant)]
        private static partial Regex SplitBranch();

        /// <summary>One slot's match of the split's right side.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"startsWith\((?<slot>\w+), @right\)", RegexOptions.CultureInvariant)]
        private static partial Regex SplitSlot();

        /// <summary>The golden file, its findable and search-text rows only.</summary>
        /// <param name="Findable">The findable rows.</param>
        /// <param name="SearchText">The search-text rows.</param>
        private sealed record Golden(IReadOnlyList<FindableRow> Findable, IReadOnlyList<SearchTextRow> SearchText);

        /// <summary>A search-text row: a rule, its searchable columns' kinds, a shape, the rows, a text, and the ids the text columns find.</summary>
        /// <param name="Name">What the row shows.</param>
        /// <param name="Rule">The display-name rule.</param>
        /// <param name="Searchable">The searchable columns, each by how it matches.</param>
        /// <param name="Shape">The tenant's language shape.</param>
        /// <param name="Rows">The rows, by camel-case member.</param>
        /// <param name="Text">The search text.</param>
        /// <param name="Found">The ids the text columns' matches find.</param>
        private sealed record SearchTextRow(
            string Name,
            FindableRule Rule,
            IReadOnlyDictionary<string, SearchKind> Searchable,
            MultilingualShape Shape,
            IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows,
            string Text,
            IReadOnlyList<int> Found);

        /// <summary>The paste-leaf golden file.</summary>
        /// <param name="Leaves">The leaves.</param>
        private sealed record PasteGolden(IReadOnlyList<PasteLeaf> Leaves);

        /// <summary>A paste leaf the client writes: a rule, a shape, the labels, and the leaf of them.</summary>
        /// <param name="Name">What the leaf shows.</param>
        /// <param name="Rule">The display-name rule.</param>
        /// <param name="Shape">The tenant's language shape.</param>
        /// <param name="Labels">The pasted labels.</param>
        /// <param name="Literals">The literals the leaf's lists hold.</param>
        /// <param name="Leaf">The leaf.</param>
        private sealed record PasteLeaf(string Name, FindableRule Rule, MultilingualShape Shape, IReadOnlyList<string> Labels, int Literals, string Leaf);

        /// <summary>A findable row: a rule, a shape, the rows, a text, and the ids a search and a paste find.</summary>
        /// <param name="Name">What the row shows.</param>
        /// <param name="Rule">The display-name rule.</param>
        /// <param name="Shape">The tenant's language shape.</param>
        /// <param name="Rows">The rows, by camel-case member.</param>
        /// <param name="Text">The search or pasted text.</param>
        /// <param name="Search">The ids the findable branches of a search find.</param>
        /// <param name="Paste">The ids the client's paste resolver finds, which the server does not run.</param>
        private sealed record FindableRow(
            string Name,
            FindableRule Rule,
            MultilingualShape Shape,
            IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows,
            string Text,
            IReadOnlyList<int> Search,
            IReadOnlyList<int> Paste);
    }

    /// <summary>A findable row's display-name rule, by property name.</summary>
    /// <param name="Key">The key property.</param>
    /// <param name="Twins">A group source's twins; null for a scalar source.</param>
    /// <param name="Source">A scalar source.</param>
    /// <param name="SourceType">The scalar source's type: <c>integer</c> or <c>date</c>.</param>
    /// <param name="Prefix">The prefix property, or null.</param>
    /// <param name="PrefixType">The prefix's type: <c>integer</c>, or text when absent.</param>
    internal sealed record FindableRule(string Key, IReadOnlyList<string>? Twins, string? Source, string? SourceType, string? Prefix, string? PrefixType);

    /// <summary>The findable model: one stack per display-name rule the golden's findable rows use.</summary>
    internal static class FindableModel
    {
        /// <summary>The account's resource: a multilingual name behind a string code prefix.</summary>
        public const string Accounts = "fd.FdAccount";

        /// <summary>The numbered account's resource: a multilingual name behind an integer prefix.</summary>
        public const string Numbered = "fd.FdNumbered";

        /// <summary>The serial's resource: an integer source.</summary>
        public const string Serials = "fd.FdSerial";

        /// <summary>The posting's resource: a date source.</summary>
        public const string Postings = "fd.FdPosting";

        /// <summary>The coded account's resource: a multilingual name behind a code that search matches from its start.</summary>
        public const string Coded = "fd.FdCoded";

        /// <summary>Composes the model's feature.</summary>
        /// <param name="tellma">The builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("findable", contribute: static contribution => contribution
                .Entity<FdAccount>()
                .Entity<FdNumbered>()
                .Entity<FdSerial>()
                .Entity<FdPosting>()
                .Entity<FdCoded>()));
        }

        /// <summary>The stack whose display name a golden rule describes.</summary>
        /// <param name="rule">The rule.</param>
        /// <returns>The stack's resource.</returns>
        /// <exception cref="ArgumentException">The model has no stack of the rule.</exception>
        public static string For(FindableRule rule)
        {
            return (rule.Source, rule.Prefix) switch
            {
                (null, "Code") => Accounts,
                (null, "Number") => Numbered,
                ("SerialNumber", null) => Serials,
                ("PostedOn", null) => Postings,
                _ => throw new ArgumentException($"The findable model has no stack of the rule {rule}.", nameof(rule)),
            };
        }

        /// <summary>The stack whose display name a golden rule describes and whose columns search matches by the given kinds.</summary>
        /// <param name="rule">The rule.</param>
        /// <param name="searchable">The searchable columns' kinds.</param>
        /// <returns>The stack's resource: the coded account's when a column is prefix-searchable.</returns>
        public static string For(FindableRule rule, IReadOnlyDictionary<string, SearchKind> searchable)
        {
            return searchable.Values.Contains(SearchKind.Prefix) ? Coded : For(rule);
        }
    }

    /// <summary>An account: a multilingual name behind a string code prefix.</summary>
    [Table("Accounts", Schema = "fd")]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Code))]
    public class FdAccount : TopLevelEntity
    {
        /// <summary>The code, the display name's prefix.</summary>
        [Column(TypeName = "varchar(50)")]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third language.</summary>
        public string? Name3 { get; set; }
    }

    /// <summary>A numbered account: a multilingual name behind an integer prefix.</summary>
    [Table("NumberedAccounts", Schema = "fd")]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Number))]
    public class FdNumbered : TopLevelEntity
    {
        /// <summary>The number, the display name's prefix.</summary>
        public int Number { get; set; }

        /// <summary>The name.</summary>
        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third language.</summary>
        public string? Name3 { get; set; }
    }

    /// <summary>A coded account: a multilingual name behind a code, which search matches from its start.</summary>
    [Table("CodedAccounts", Schema = "fd")]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Code))]
    public class FdCoded : TopLevelEntity
    {
        /// <summary>The code, the display name's prefix, which search matches from its start.</summary>
        [Column(TypeName = "varchar(50)")]
        [Searchable(SearchKind.Prefix)]
        public string Code { get; set; } = null!;

        /// <summary>The name, which search matches anywhere.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third language.</summary>
        public string? Name3 { get; set; }
    }

    /// <summary>A serial: an integer display name.</summary>
    [Table("Serials", Schema = "fd")]
    [TableType]
    [DisplayNameSource(nameof(SerialNumber))]
    public class FdSerial : TopLevelEntity
    {
        /// <summary>The serial number, the display name.</summary>
        public int SerialNumber { get; set; }
    }

    /// <summary>A posting: a date display name.</summary>
    [Table("Postings", Schema = "fd")]
    [TableType]
    [DisplayNameSource(nameof(PostedOn))]
    public class FdPosting : TopLevelEntity
    {
        /// <summary>The date it was posted on, the display name.</summary>
        public DateOnly PostedOn { get; set; }
    }
}
