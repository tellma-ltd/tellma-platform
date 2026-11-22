// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Reports;

namespace Tellma.Core.Reports
{
    // Spec 0033 §3.1: the document binds strictly into its records under the platform's JSON
    // options: an unknown member, a reserved one or a value of the wrong shape is a binding failure,
    // which names the member by its RFC 6901 pointer. Spec 0033 §2.1: a stored report's
    // QueryexVersion follows its expressions (the filter, each having, each term's expr, each order
    // expression and each parameter default); paths and drill columns are column keys, not
    // expressions.
    /// <summary>Binds stored reports' definition documents and reads their query language expressions.</summary>
    internal static class ReportDocuments
    {
        /// <summary>Binds a document strictly.</summary>
        /// <param name="json">The document's JSON.</param>
        /// <param name="document">The bound document; null when binding failed.</param>
        /// <param name="pointer">The RFC 6901 pointer of the member that failed to bind; the empty pointer for the document itself; null on success.</param>
        /// <returns>True when the document bound.</returns>
        public static bool TryParse(string json, out ReportDocument? document, out string? pointer)
        {
            ArgumentNullException.ThrowIfNull(json);
            try
            {
                document = JsonSerializer.Deserialize<ReportDocument>(json, TellmaJson.Options);
                pointer = document is null ? string.Empty : null;
                return document is not null;
            }
            catch (JsonException exception)
            {
                document = null;
                pointer = Pointer(exception.Path);
                return false;
            }
        }

        /// <summary>The query language expressions of a document, in document order.</summary>
        /// <param name="document">The document.</param>
        /// <returns>The expressions; an absent one is skipped.</returns>
        public static IEnumerable<string> Expressions(ReportDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            // --- The parameters' defaults and the row filter ---
            foreach (ReportParameter? parameter in document.Parameters ?? [])
            {
                if (parameter?.Default is string value)
                {
                    yield return value;
                }
            }

            if (document.Filter is string filter)
            {
                yield return filter;
            }

            // --- The kind block's terms, orderings and having ---
            IEnumerable<string?> block = document switch
            {
                { List: ReportList list } => Terms(list.Columns).Concat(Orders(list.OrderBy)),
                { Summary: ReportSummary summary } => Terms(summary.Columns).Concat(Orders(summary.OrderBy)).Append(summary.Having),
                { Pivot: ReportPivot pivot } => Dimensions(pivot.Rows).Concat(Dimensions(pivot.Columns)).Concat(Terms(pivot.Measures)).Append(pivot.Having),
                { Chart: ReportChart chart } => Dimensions(chart.Dimensions).Concat(Terms([chart.Measure])).Append(chart.Having),
                _ => [],
            };
            foreach (string? expression in block)
            {
                if (expression is not null)
                {
                    yield return expression;
                }
            }
        }

        /// <summary>Converts a JSON path, as a binding failure names it, to an RFC 6901 pointer.</summary>
        /// <param name="path">The path, such as <c>$.list.columns[0].running</c> or <c>$['odd name']</c>; null for the document.</param>
        /// <returns>The pointer, such as <c>/list/columns/0/running</c>; the empty pointer for the document.</returns>
        internal static string Pointer(string? path)
        {
            if (string.IsNullOrEmpty(path) || path == "$")
            {
                return string.Empty;
            }

            StringBuilder pointer = new();
            int index = path[0] == '$' ? 1 : 0;
            while (index < path.Length)
            {
                // --- A dotted member: up to the next separator ---
                if (path[index] == '.')
                {
                    int end = path.IndexOfAny(['.', '['], index + 1);
                    end = end < 0 ? path.Length : end;
                    Append(pointer, path[(index + 1)..end]);
                    index = end;
                }

                // --- A quoted member: ['name'], a quote inside escaped as \' ---
                else if (path[index] == '[' && index + 1 < path.Length && path[index + 1] == '\'')
                {
                    int end = path.IndexOf("']", index + 2, StringComparison.Ordinal);
                    end = end < 0 ? path.Length : end;
                    Append(pointer, path[(index + 2)..end].Replace("\\'", "'", StringComparison.Ordinal));
                    index = Math.Min(path.Length, end + 2);
                }

                // --- An array index: [n] ---
                else if (path[index] == '[')
                {
                    int end = path.IndexOf(']', index + 1);
                    end = end < 0 ? path.Length : end;
                    Append(pointer, path[(index + 1)..end]);
                    index = Math.Min(path.Length, end + 1);
                }
                else
                {
                    // An unexpected character: the rest names one member.
                    Append(pointer, path[index..]);
                    break;
                }
            }

            return pointer.ToString();
        }

        /// <summary>Appends one reference token, escaped per RFC 6901.</summary>
        /// <param name="pointer">The pointer.</param>
        /// <param name="token">The token.</param>
        private static void Append(StringBuilder pointer, string token)
        {
            _ = pointer.Append('/').Append(token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
        }

        /// <summary>The expressions of some terms.</summary>
        /// <param name="terms">The terms; null for none.</param>
        /// <returns>The expressions, null where a term has none.</returns>
        private static IEnumerable<string?> Terms(IEnumerable<ReportTerm?>? terms)
        {
            return (terms ?? []).Select(static term => term?.Expr);
        }

        /// <summary>The expressions of some dimensions and their attributes.</summary>
        /// <param name="dimensions">The dimensions; null for none.</param>
        /// <returns>The expressions, null where a term has none.</returns>
        private static IEnumerable<string?> Dimensions(IEnumerable<ReportDimension?>? dimensions)
        {
            return (dimensions ?? []).SelectMany(static dimension => Terms([dimension]).Concat(Terms(dimension?.Attributes)));
        }

        /// <summary>The expressions of some orderings.</summary>
        /// <param name="orders">The orderings; null for none.</param>
        /// <returns>The expressions, null where an ordering names a column.</returns>
        private static IEnumerable<string?> Orders(IEnumerable<ReportOrder?>? orders)
        {
            return (orders ?? []).Select(static order => order?.Expr);
        }
    }
}
