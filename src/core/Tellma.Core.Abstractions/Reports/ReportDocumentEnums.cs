// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Reports
{
    // Spec 0033 §3.1: the definition document's vocabularies, each member under its camelCase wire
    // spelling, so a document and a run's result spell them alike.

    /// <summary>How a term's values display.</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Integer is the document's display name, spelled integer on the wire; no consumer reads it as the integer type.")]
    public enum ReportDisplay
    {
        /// <summary>A number with fraction digits.</summary>
        [JsonStringEnumMemberName("number")]
        Number,

        /// <summary>A whole number.</summary>
        [JsonStringEnumMemberName("integer")]
        Integer,

        /// <summary>A fraction shown as a percentage.</summary>
        [JsonStringEnumMemberName("percent")]
        Percent,

        /// <summary>A month number shown as the month's name.</summary>
        [JsonStringEnumMemberName("monthName")]
        MonthName,

        /// <summary>A weekday number shown as the weekday's name.</summary>
        [JsonStringEnumMemberName("weekdayName")]
        WeekdayName,

        /// <summary>Plain text.</summary>
        [JsonStringEnumMemberName("text")]
        Text,

        /// <summary>A value shown as a label, from a named enum or inline choices.</summary>
        [JsonStringEnumMemberName("enum")]
        Enum,

        /// <summary>A date.</summary>
        [JsonStringEnumMemberName("date")]
        Date,

        /// <summary>A date shown as its month.</summary>
        [JsonStringEnumMemberName("month")]
        Month,

        /// <summary>A date shown as its quarter.</summary>
        [JsonStringEnumMemberName("quarter")]
        Quarter,

        /// <summary>A date shown as its year.</summary>
        [JsonStringEnumMemberName("year")]
        Year,

        /// <summary>A date and a time of day, with no offset.</summary>
        [JsonStringEnumMemberName("datetime")]
        DateTime,

        /// <summary>An instant, shown in the viewer's time zone.</summary>
        [JsonStringEnumMemberName("instant")]
        Instant,

        /// <summary>A boolean.</summary>
        [JsonStringEnumMemberName("bool")]
        Bool,
    }

    /// <summary>The style of a date display.</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Long is the established name of the date style in every formatting library; no consumer reads it as the integer type.")]
    public enum ReportDateStyle
    {
        /// <summary>Digits only.</summary>
        [JsonStringEnumMemberName("numeric")]
        Numeric,

        /// <summary>An abbreviated month name.</summary>
        [JsonStringEnumMemberName("medium")]
        Medium,

        /// <summary>The full month name.</summary>
        [JsonStringEnumMemberName("long")]
        Long,
    }

    /// <summary>The direction of an order.</summary>
    public enum ReportSortOrder
    {
        /// <summary>Ascending.</summary>
        [JsonStringEnumMemberName("asc")]
        Asc,

        /// <summary>Descending.</summary>
        [JsonStringEnumMemberName("desc")]
        Desc,
    }

    /// <summary>A chart's type.</summary>
    public enum ReportChartType
    {
        /// <summary>Vertical bars.</summary>
        [JsonStringEnumMemberName("column")]
        Column,

        /// <summary>Horizontal bars.</summary>
        [JsonStringEnumMemberName("bar")]
        Bar,

        /// <summary>A line through the points.</summary>
        [JsonStringEnumMemberName("line")]
        Line,

        /// <summary>A filled line.</summary>
        [JsonStringEnumMemberName("area")]
        Area,

        /// <summary>A pie.</summary>
        [JsonStringEnumMemberName("pie")]
        Pie,

        /// <summary>A ring.</summary>
        [JsonStringEnumMemberName("doughnut")]
        Doughnut,

        /// <summary>One value on a card, with no dimension.</summary>
        [JsonStringEnumMemberName("card")]
        Card,
    }

    /// <summary>What a chart's points are ordered by.</summary>
    public enum ReportChartOrderBy
    {
        /// <summary>The dimension's members.</summary>
        [JsonStringEnumMemberName("dimension")]
        Dimension,

        /// <summary>The measure's values.</summary>
        [JsonStringEnumMemberName("measure")]
        Measure,
    }

    /// <summary>How the series of a two-dimension chart stack.</summary>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "The chart option's name, stack on the wire; a stacking mode, not a stack collection.")]
    public enum ReportStack
    {
        /// <summary>Side by side.</summary>
        [JsonStringEnumMemberName("none")]
        None,

        /// <summary>On top of one another.</summary>
        [JsonStringEnumMemberName("stacked")]
        Stacked,

        /// <summary>On top of one another, each point's stack scaled to the whole.</summary>
        [JsonStringEnumMemberName("normalized")]
        Normalized,
    }

    /// <summary>Where a chart's legend shows.</summary>
    public enum ReportLegend
    {
        /// <summary>Where the renderer finds room; hidden for one series.</summary>
        [JsonStringEnumMemberName("auto")]
        Auto,

        /// <summary>Hidden.</summary>
        [JsonStringEnumMemberName("none")]
        None,

        /// <summary>Above the plot.</summary>
        [JsonStringEnumMemberName("top")]
        Top,

        /// <summary>Below the plot.</summary>
        [JsonStringEnumMemberName("bottom")]
        Bottom,

        /// <summary>Before the plot, in the reading direction.</summary>
        [JsonStringEnumMemberName("start")]
        Start,

        /// <summary>After the plot, in the reading direction.</summary>
        [JsonStringEnumMemberName("end")]
        End,
    }

    /// <summary>The palette a chart's series take.</summary>
    public enum ReportPalette
    {
        /// <summary>Distinct hues for unordered series.</summary>
        [JsonStringEnumMemberName("categorical")]
        Categorical,

        /// <summary>One hue's ramp for ordered series.</summary>
        [JsonStringEnumMemberName("sequential")]
        Sequential,

        /// <summary>Two hues' ramps meeting at a midpoint.</summary>
        [JsonStringEnumMemberName("diverging")]
        Diverging,
    }

    /// <summary>The tone of a chart's threshold line.</summary>
    public enum ReportTone
    {
        /// <summary>A good value.</summary>
        [JsonStringEnumMemberName("positive")]
        Positive,

        /// <summary>A value to watch.</summary>
        [JsonStringEnumMemberName("warning")]
        Warning,

        /// <summary>A bad value.</summary>
        [JsonStringEnumMemberName("negative")]
        Negative,
    }
}
