// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Text.RegularExpressions;

namespace Tellma.Testing.Support.Observability
{
    /// <summary>One checked-in alert query: its file's name and its text without the comment lines.</summary>
    /// <param name="FileName">The file's name.</param>
    /// <param name="Body">The query's text, comment lines dropped.</param>
    public sealed record AlertQuery(string FileName, string Body);

    /// <summary>
    ///     Cross-checks checked-in alert queries (Kusto, over Application Insights' <c>customMetrics</c>)
    ///     against the instruments, tags and tag values the code can emit, so a telemetry rename
    ///     fails a suite instead of leaving an alert that reports zero forever. Each suite says what
    ///     its code emits; the reading of the queries is shared.
    /// </summary>
    public static partial class AlertQueries
    {
        /// <summary>Reads every <c>.kql</c> file of a folder, comment lines dropped.</summary>
        /// <remarks>
        ///     Comment lines are dropped because an instrument merely mentioned in the prose above a
        ///     query is not queried, and the prose also quotes words that are not tag values at all.
        /// </remarks>
        /// <param name="directory">The folder, usually the suite's output folder the queries are copied to.</param>
        /// <returns>The queries, ordered by file name.</returns>
        public static IReadOnlyList<AlertQuery> Load(string directory)
        {
            return [.. Directory.GetFiles(directory, "*.kql")
                .Order(StringComparer.Ordinal)
                .Select(static file => new AlertQuery(Path.GetFileName(file), Body(File.ReadAllText(file))))];
        }

        /// <summary>The metric names a query filters on (<c>name == "…"</c>).</summary>
        /// <param name="query">The query.</param>
        /// <returns>The names.</returns>
        public static IEnumerable<string> Metrics(AlertQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            return MetricNameReference().Matches(query.Body).Select(static match => match.Groups["name"].Value);
        }

        /// <summary>The dimension keys a query reads (<c>customDimensions["…"]</c>).</summary>
        /// <param name="query">The query.</param>
        /// <returns>The keys.</returns>
        public static IEnumerable<string> Dimensions(AlertQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            return DimensionReference().Matches(query.Body).Select(static match => match.Groups["name"].Value);
        }

        /// <summary>The quoted values a query compares against: every string literal but its metric names and dimension keys.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The values.</returns>
        public static IEnumerable<string> Values(AlertQuery query)
        {
            ArgumentNullException.ThrowIfNull(query);
            HashSet<string> names = new(Metrics(query).Concat(Dimensions(query)), StringComparer.Ordinal);
            return QuotedLiteral().Matches(query.Body)
                .Select(static match => match.Groups["value"].Value)
                .Where(value => !names.Contains(value));
        }

        /// <summary>The string literals of a fragment of a query, such as a list bound with <c>let</c>.</summary>
        /// <param name="text">The fragment.</param>
        /// <returns>The literals' values.</returns>
        public static IEnumerable<string> Literals(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return QuotedLiteral().Matches(text).Select(static match => match.Groups["value"].Value);
        }

        /// <summary>
        ///     Every metric, dimension and value of the queries that the code cannot emit, each named by
        ///     its file; empty when every query resolves.
        /// </summary>
        /// <param name="queries">The queries.</param>
        /// <param name="instruments">The instrument names the code declares.</param>
        /// <param name="tags">The tag keys the code declares.</param>
        /// <param name="values">The tag values the code can emit.</param>
        /// <returns>The unresolved references, as <c>file: kind 'text'</c>.</returns>
        public static IReadOnlyList<string> Unresolved(
            IEnumerable<AlertQuery> queries, IReadOnlySet<string> instruments, IReadOnlySet<string> tags, IReadOnlySet<string> values)
        {
            ArgumentNullException.ThrowIfNull(queries);
            ArgumentNullException.ThrowIfNull(instruments);
            ArgumentNullException.ThrowIfNull(tags);
            ArgumentNullException.ThrowIfNull(values);

            List<string> unresolved = [];
            foreach (AlertQuery query in queries)
            {
                unresolved.AddRange(Metrics(query).Where(name => !instruments.Contains(name)).Select(name => $"{query.FileName}: metric '{name}'"));
                unresolved.AddRange(Dimensions(query).Where(name => !tags.Contains(name)).Select(name => $"{query.FileName}: dimension '{name}'"));
                unresolved.AddRange(Values(query).Where(value => !values.Contains(value)).Select(value => $"{query.FileName}: value '{value}'"));
            }

            return unresolved;
        }

        /// <summary>The instruments no query names, in ordinal order.</summary>
        /// <param name="queries">The queries.</param>
        /// <param name="instruments">The instrument names the code declares.</param>
        /// <returns>The uncovered instruments.</returns>
        public static IReadOnlyList<string> Uncovered(IEnumerable<AlertQuery> queries, IEnumerable<string> instruments)
        {
            ArgumentNullException.ThrowIfNull(queries);
            ArgumentNullException.ThrowIfNull(instruments);
            HashSet<string> queried = new(queries.SelectMany(Metrics), StringComparer.Ordinal);
            return [.. instruments.Where(instrument => !queried.Contains(instrument)).Distinct().Order(StringComparer.Ordinal)];
        }

        /// <summary>The values of the public string constants a set of types declares, the types themselves public or internal, whose names pass a filter.</summary>
        /// <param name="types">The types.</param>
        /// <param name="nameFilter">Which constants, by name.</param>
        /// <returns>The values.</returns>
        public static HashSet<string> Constants(IEnumerable<Type> types, Func<string, bool> nameFilter)
        {
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(nameFilter);
            HashSet<string> values = new(StringComparer.Ordinal);
            foreach (Type type in types)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.IsLiteral && nameFilter(field.Name) && field.GetRawConstantValue() is string value)
                    {
                        _ = values.Add(value);
                    }
                }
            }

            return values;
        }

        /// <summary>A query's text without its comment lines.</summary>
        /// <param name="text">The file's text.</param>
        /// <returns>The body.</returns>
        private static string Body(string text)
        {
            return string.Join(
                '\n',
                text.ReplaceLineEndings("\n").Split('\n').Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        }

        [GeneratedRegex("name\\s*==\\s*\"(?<name>[^\"]+)\"")]
        private static partial Regex MetricNameReference();

        [GeneratedRegex("""customDimensions\["(?<name>[^"]+)"\]""")]
        private static partial Regex DimensionReference();

        [GeneratedRegex("\"(?<value>[^\"]*)\"")]
        private static partial Regex QuotedLiteral();
    }
}
