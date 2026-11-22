// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Reports
{
    // Spec 0033 §3.1: the document's records mirror its camelCase JSON member for member, and are the
    // only code that reads or writes it. Each refuses an unknown member, so a typo never drops a
    // filter; the reserved "running" member is left out of the terms for the same refusal. The
    // platform's JSON options read and write them; the enums carry their wire spellings.

    /// <summary>A stored report's definition document: its parameters, its row filter, its one kind block and its drill-through columns.</summary>
    /// <param name="FormatVersion">The document's format version; 1.</param>
    /// <param name="Parameters">The parameters a run binds; null for none.</param>
    /// <param name="Filter">A query language predicate over the root, at row level; null for none.</param>
    /// <param name="List">The block of a list report.</param>
    /// <param name="Summary">The block of a summary report.</param>
    /// <param name="Pivot">The block of a pivot report.</param>
    /// <param name="Chart">The block of a chart report.</param>
    /// <param name="DrillColumns">The root's column keys a drill-through lists; summary, pivot and chart only; null for the default.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportDocument(
        int FormatVersion,
        IReadOnlyList<ReportParameter>? Parameters,
        string? Filter,
        ReportList? List,
        ReportSummary? Summary,
        ReportPivot? Pivot,
        ReportChart? Chart,
        IReadOnlyList<string>? DrillColumns);

    /// <summary>A column, dimension, measure or attribute of a report: a column key or an expression, with its labels and display.</summary>
    /// <param name="Path">A column key of the root; exactly one of it and <paramref name="Expr" />.</param>
    /// <param name="Expr">A query language value expression; exactly one of it and <paramref name="Path" />.</param>
    /// <param name="Label">The label in content slot 1, at most 255 characters.</param>
    /// <param name="Label2">The label in content slot 2.</param>
    /// <param name="Label3">The label in content slot 3.</param>
    /// <param name="LabelKey">A string resource key instead of the three labels, as seeds use.</param>
    /// <param name="Display">How the values display; null for the default display.</param>
    /// <param name="Format">The display's parameters.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public record ReportTerm(
        string? Path,
        string? Expr,
        string? Label,
        string? Label2,
        string? Label3,
        string? LabelKey,
        ReportDisplay? Display,
        ReportFormat? Format);

    /// <summary>A grouping term of a pivot axis or a chart: a term with its member order, expansion, tree and attributes.</summary>
    /// <param name="Path">A column key of the root; exactly one of it and <paramref name="Expr" />.</param>
    /// <param name="Expr">A query language value expression; exactly one of it and <paramref name="Path" />.</param>
    /// <param name="Label">The label in content slot 1.</param>
    /// <param name="Label2">The label in content slot 2.</param>
    /// <param name="Label3">The label in content slot 3.</param>
    /// <param name="LabelKey">A string resource key instead of the three labels.</param>
    /// <param name="Display">How the members display; null for the default display.</param>
    /// <param name="Format">The display's parameters.</param>
    /// <param name="Order">The member order; null for ascending.</param>
    /// <param name="Expand">Whether a pivot opens the dimension expanded; null for true.</param>
    /// <param name="Tree">Whether a navigation to a tree entity groups over the full tree; null for true there.</param>
    /// <param name="Attributes">The columns a pivot with one row dimension shows before its measures.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportDimension(
        string? Path,
        string? Expr,
        string? Label,
        string? Label2,
        string? Label3,
        string? LabelKey,
        ReportDisplay? Display,
        ReportFormat? Format,
        ReportSortOrder? Order,
        bool? Expand,
        bool? Tree,
        IReadOnlyList<ReportTerm>? Attributes)
        : ReportTerm(Path, Expr, Label, Label2, Label3, LabelKey, Display, Format);

    /// <summary>The parameters of a display.</summary>
    /// <param name="Decimals">The fraction digits of a numeric display.</param>
    /// <param name="Grouping">Whether a numeric display groups its digits; null for true unless inferred otherwise.</param>
    /// <param name="DateStyle">The style of a date display.</param>
    /// <param name="Calendar">The calendar of a month, quarter, year or month-name display: <c>gc</c>, <c>uq</c> or <c>et</c>.</param>
    /// <param name="Enum">The named enum whose labels an <c>enum</c> display shows.</param>
    /// <param name="Choices">The inline labels an <c>enum</c> display shows instead.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportFormat(
        int? Decimals,
        bool? Grouping,
        ReportDateStyle? DateStyle,
        string? Calendar,
        string? Enum,
        IReadOnlyList<ReportChoice>? Choices);

    /// <summary>One value's label under an <c>enum</c> display.</summary>
    /// <param name="Value">The term's value: a string, a number or a boolean.</param>
    /// <param name="Label">The label in content slot 1.</param>
    /// <param name="Label2">The label in content slot 2.</param>
    /// <param name="Label3">The label in content slot 3.</param>
    /// <param name="LabelKey">A string resource key instead of the three labels.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportChoice(JsonElement Value, string? Label, string? Label2, string? Label3, string? LabelKey);

    /// <summary>One ordering of a list or summary: a column by its index, or an expression.</summary>
    /// <param name="Term">A zero-based index into the columns; exactly one of it and <paramref name="Expr" />.</param>
    /// <param name="Expr">A query language expression; exactly one of it and <paramref name="Term" />.</param>
    /// <param name="Desc">Whether the order descends; null for ascending.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportOrder(int? Term, string? Expr, bool? Desc);

    /// <summary>The block of a list report: row-level columns.</summary>
    /// <param name="Columns">The columns.</param>
    /// <param name="OrderBy">The ordering; null for the default.</param>
    /// <param name="Top">The most rows the report shows; null for all.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportList(IReadOnlyList<ReportTerm> Columns, IReadOnlyList<ReportOrder>? OrderBy, int? Top);

    /// <summary>The block of a summary report: grouping keys and measures.</summary>
    /// <param name="Columns">The keys and measures.</param>
    /// <param name="OrderBy">The ordering; null for the default.</param>
    /// <param name="Having">A query language predicate over the groups.</param>
    /// <param name="Top">The most groups the report shows; null for all.</param>
    /// <param name="Totals">Whether a totals row shows when the whole result fits on one page.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportSummary(IReadOnlyList<ReportTerm> Columns, IReadOnlyList<ReportOrder>? OrderBy, string? Having, int? Top, bool? Totals);

    /// <summary>The block of a pivot report: row and column dimensions, and measures in the cells.</summary>
    /// <param name="Rows">The row dimensions.</param>
    /// <param name="Columns">The column dimensions.</param>
    /// <param name="Measures">The measures.</param>
    /// <param name="Having">A query language predicate over the members.</param>
    /// <param name="RowTotals">Whether the grand total row shows.</param>
    /// <param name="ColumnTotals">Whether the grand total column shows.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportPivot(
        IReadOnlyList<ReportDimension> Rows,
        IReadOnlyList<ReportDimension> Columns,
        IReadOnlyList<ReportTerm> Measures,
        string? Having,
        bool? RowTotals,
        bool? ColumnTotals);

    /// <summary>The block of a chart report: one measure over its dimensions.</summary>
    /// <param name="Type">The chart's type.</param>
    /// <param name="Dimensions">The dimensions, as many as the type takes.</param>
    /// <param name="Measure">The one numeric measure.</param>
    /// <param name="Having">A query language predicate over the points.</param>
    /// <param name="Top">The most points the chart plots; null for all.</param>
    /// <param name="OrderBy">What the points are ordered by; null for the dimension.</param>
    /// <param name="Order">The direction of the order; null for ascending.</param>
    /// <param name="Options">How the chart draws.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportChart(
        ReportChartType Type,
        IReadOnlyList<ReportDimension> Dimensions,
        ReportTerm Measure,
        string? Having,
        int? Top,
        ReportChartOrderBy? OrderBy,
        ReportSortOrder? Order,
        ReportChartOptions? Options);

    /// <summary>How a chart draws.</summary>
    /// <param name="Stack">How the series of a two-dimension chart stack.</param>
    /// <param name="Legend">Where the legend shows.</param>
    /// <param name="Markers">Whether a line or area chart marks its points.</param>
    /// <param name="Labels">Whether the points show their values.</param>
    /// <param name="Palette">The palette the series take.</param>
    /// <param name="Min">The value axis's lower bound; null for automatic.</param>
    /// <param name="Max">The value axis's upper bound; null for automatic.</param>
    /// <param name="Thresholds">The lines drawn across the value axis.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportChartOptions(
        ReportStack? Stack,
        ReportLegend? Legend,
        bool? Markers,
        bool? Labels,
        ReportPalette? Palette,
        double? Min,
        double? Max,
        IReadOnlyList<ReportThreshold>? Thresholds);

    /// <summary>A line drawn across a chart's value axis.</summary>
    /// <param name="Value">Where the line is drawn.</param>
    /// <param name="Tone">The line's tone.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportThreshold(double Value, ReportTone Tone);

    /// <summary>A parameter a run binds, which the document's expressions name with <c>@key</c>.</summary>
    /// <param name="Key">The parameter's name.</param>
    /// <param name="Label">The label in content slot 1.</param>
    /// <param name="Label2">The label in content slot 2.</param>
    /// <param name="Label3">The label in content slot 3.</param>
    /// <param name="LabelKey">A string resource key instead of the three labels.</param>
    /// <param name="Required">Whether a run must bind it; null for false.</param>
    /// <param name="Default">A query language constant expression a run takes when it binds none.</param>
    /// <param name="Type">The parameter's query language type; null to infer it.</param>
    /// <param name="Entity">The entity a key parameter names; null to infer it.</param>
    /// <param name="Display">How the parameter's editor displays its value.</param>
    /// <param name="Format">The display's parameters.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ReportParameter(
        string Key,
        string? Label,
        string? Label2,
        string? Label3,
        string? LabelKey,
        bool? Required,
        string? Default,
        string? Type,
        string? Entity,
        ReportDisplay? Display,
        ReportFormat? Format);
}
