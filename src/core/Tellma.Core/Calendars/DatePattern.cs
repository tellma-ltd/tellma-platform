// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Text;

namespace Tellma.Core.Calendars
{
    /// <summary>What a <see cref="DatePatternToken" /> stands for.</summary>
    internal enum DatePatternTokenKind
    {
        /// <summary>Literal text, already cleaned.</summary>
        Literal,

        /// <summary>The day of the month as a number; width 1 unpadded, 2 padded to two digits.</summary>
        Day,

        /// <summary>The month; width 1 or 2 a number (unpadded, padded), 3 the abbreviated name, 4 the full name.</summary>
        Month,

        /// <summary>The year, always in full and padded to four digits.</summary>
        Year,

        /// <summary>The weekday's name; width 3 abbreviated, 4 full.</summary>
        Weekday,

        /// <summary>The culture's date separator.</summary>
        DateSeparator,
    }

    /// <summary>One element of a <see cref="DatePattern" />.</summary>
    /// <param name="Kind">What the token stands for.</param>
    /// <param name="Width">The width of a field token; 0 for literal text and the separator.</param>
    /// <param name="Text">The text of a literal token; empty otherwise.</param>
    internal readonly record struct DatePatternToken(DatePatternTokenKind Kind, int Width, string Text);

    /// <summary>
    ///     A date pattern of the framework's custom format grammar (<c>dddd, d MMMM yyyy</c>), read
    ///     into tokens the calendar systems fill with their own year, month and day. Every pattern
    ///     holds a day, a month and a year.
    /// </summary>
    /// <remarks>
    ///     Reading a culture's pattern drops its era (CLDR appends one to the Arabic patterns), cleans
    ///     its literals (<see cref="CalendarText.Clean" />) and widens every year to the full year, so
    ///     a rendered date is never ambiguous and always reads back.
    /// </remarks>
    internal sealed class DatePattern
    {
        /// <summary>Creates a pattern from tokens already normalised.</summary>
        /// <param name="tokens">The tokens.</param>
        private DatePattern(ImmutableArray<DatePatternToken> tokens)
        {
            Tokens = tokens;
        }

        /// <summary>The tokens, in order.</summary>
        public ImmutableArray<DatePatternToken> Tokens { get; }

        /// <summary>Whether the pattern holds a numeric day, in which case a month name takes its genitive form.</summary>
        public bool HasDayNumber => Tokens.Any(static t => t.Kind == DatePatternTokenKind.Day);

        /// <summary>Whether the pattern holds a day, a month and a year.</summary>
        public bool IsComplete => Has(DatePatternTokenKind.Day) && Has(DatePatternTokenKind.Month) && Has(DatePatternTokenKind.Year);

        /// <summary>The order in which the day, the month and the year first appear.</summary>
        public IReadOnlyList<DatePatternTokenKind> FieldOrder =>
            [.. Tokens
                .Where(static t => t.Kind is DatePatternTokenKind.Day or DatePatternTokenKind.Month or DatePatternTokenKind.Year)
                .Select(static t => t.Kind)
                .Distinct()];

        /// <summary>Reads a pattern of the framework's custom date format grammar.</summary>
        /// <param name="pattern">The pattern.</param>
        /// <returns>The pattern's tokens, normalised.</returns>
        public static DatePattern Parse(string pattern)
        {
            List<DatePatternToken> tokens = [];
            StringBuilder literal = new();

            // --- Tokenise: field letters by run length, quotes and escapes as literal text ---
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                switch (c)
                {
                    case 'd' or 'M' or 'y' or 'g':
                        int run = RunLength(pattern, i);
                        FlushLiteral(tokens, literal);
                        AddField(tokens, c, run);
                        i += run;
                        break;

                    case '/':
                        FlushLiteral(tokens, literal);
                        tokens.Add(new DatePatternToken(DatePatternTokenKind.DateSeparator, 0, string.Empty));
                        i++;
                        break;

                    case '\'' or '"':
                        // A quoted run is literal text; a backslash inside it escapes the next character.
                        i++;
                        while (i < pattern.Length && pattern[i] != c)
                        {
                            if (pattern[i] == '\\' && i + 1 < pattern.Length)
                            {
                                i++;
                            }

                            _ = literal.Append(pattern[i]);
                            i++;
                        }

                        i++;
                        break;

                    case '\\':
                        // An escaped character is literal text.
                        if (i + 1 < pattern.Length)
                        {
                            _ = literal.Append(pattern[i + 1]);
                        }

                        i += 2;
                        break;

                    case '%':
                        // Marks a lone specifier ("%d"); it adds nothing of its own.
                        i++;
                        break;

                    default:
                        _ = literal.Append(c);
                        i++;
                        break;
                }
            }

            FlushLiteral(tokens, literal);
            return new DatePattern(Normalise(tokens));
        }

        /// <summary>The numeric form of this pattern: month names become padded numbers and the weekday goes.</summary>
        /// <returns>The numeric pattern.</returns>
        public DatePattern ToNumeric()
        {
            DatePattern withoutWeekday = WithoutWeekday();
            return new DatePattern(Normalise(withoutWeekday.Tokens.Select(static t =>
                t.Kind == DatePatternTokenKind.Month && t.Width >= 3 ? t with { Width = 2 } : t)));
        }

        /// <summary>This pattern with the weekday and the punctuation that joins it to the rest removed.</summary>
        /// <returns>The pattern without a weekday; this instance when it has none.</returns>
        public DatePattern WithoutWeekday()
        {
            List<DatePatternToken> tokens = [.. Tokens];
            int index = tokens.FindIndex(static t => t.Kind == DatePatternTokenKind.Weekday);
            if (index < 0)
            {
                return this;
            }

            // The weekday is joined to the date by a run of punctuation and spaces ("dddd, d MMMM",
            // "d MMMM y, dddd"); a literal holding letters ("日" before a trailing weekday) stays.
            tokens.RemoveAt(index);
            if (index < tokens.Count && IsJoiner(tokens[index]))
            {
                tokens.RemoveAt(index);
            }
            else if (index > 0 && IsJoiner(tokens[index - 1]))
            {
                tokens.RemoveAt(index - 1);
            }

            return new DatePattern(Normalise(tokens)).WithoutWeekday();
        }

        /// <summary>This pattern with a weekday: unchanged when it has one, else with the full weekday and a comma before it.</summary>
        /// <returns>A pattern holding a weekday.</returns>
        public DatePattern WithWeekday()
        {
            return Has(DatePatternTokenKind.Weekday)
                ? this
                : new DatePattern(Normalise(
                    [new DatePatternToken(DatePatternTokenKind.Weekday, 4, string.Empty), Literal(", "), .. Tokens]));
        }

        /// <summary>This pattern with full month names shortened to abbreviated ones.</summary>
        /// <returns>The abbreviated pattern.</returns>
        public DatePattern WithAbbreviatedMonth()
        {
            return new DatePattern(Normalise(Tokens.Select(static t =>
                t.Kind == DatePatternTokenKind.Month && t.Width == 4 ? t with { Width = 3 } : t)));
        }

        /// <summary>
        ///     Writes the pattern back in the framework's custom format grammar, with every literal
        ///     quoted. The framework renders it, for a Gregorian date, exactly as the calendar systems
        ///     do, which is what the tests compare against.
        /// </summary>
        /// <returns>The pattern text.</returns>
        public override string ToString()
        {
            StringBuilder builder = new();
            foreach (DatePatternToken token in Tokens)
            {
                _ = token.Kind switch
                {
                    DatePatternTokenKind.Literal => builder.Append('\'').Append(token.Text.Replace("'", "\\'", StringComparison.Ordinal)).Append('\''),
                    DatePatternTokenKind.Day => builder.Append('d', token.Width),
                    DatePatternTokenKind.Month => builder.Append('M', token.Width),
                    DatePatternTokenKind.Year => builder.Append("yyyy"),
                    DatePatternTokenKind.Weekday => builder.Append('d', token.Width),
                    DatePatternTokenKind.DateSeparator => builder.Append('/'),
                    _ => builder,
                };
            }

            return builder.ToString();
        }

        /// <summary>Whether the pattern holds a token of a kind.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>True when it does.</returns>
        private bool Has(DatePatternTokenKind kind)
        {
            return Tokens.Any(t => t.Kind == kind);
        }

        /// <summary>The length of the run of one letter starting at an index.</summary>
        /// <param name="pattern">The pattern.</param>
        /// <param name="start">The index of the run's first letter.</param>
        /// <returns>The run's length, at least 1.</returns>
        private static int RunLength(string pattern, int start)
        {
            int end = start;
            while (end < pattern.Length && pattern[end] == pattern[start])
            {
                end++;
            }

            return end - start;
        }

        /// <summary>Adds the field token a run of one specifier letter stands for.</summary>
        /// <param name="tokens">The tokens so far.</param>
        /// <param name="letter">The letter.</param>
        /// <param name="run">The run's length.</param>
        private static void AddField(List<DatePatternToken> tokens, char letter, int run)
        {
            switch (letter)
            {
                case 'd':
                    tokens.Add(run <= 2
                        ? new DatePatternToken(DatePatternTokenKind.Day, run, string.Empty)
                        : new DatePatternToken(DatePatternTokenKind.Weekday, run == 3 ? 3 : 4, string.Empty));
                    break;

                case 'M':
                    tokens.Add(new DatePatternToken(DatePatternTokenKind.Month, Math.Min(run, 4), string.Empty));
                    break;

                case 'y':
                    tokens.Add(new DatePatternToken(DatePatternTokenKind.Year, 4, string.Empty));
                    break;

                default:
                    // The era ('g'): never rendered. The spaces around it are trimmed by Normalise.
                    break;
            }
        }

        /// <summary>Moves accumulated literal text into a token.</summary>
        /// <param name="tokens">The tokens so far.</param>
        /// <param name="literal">The accumulated text, cleared afterwards.</param>
        private static void FlushLiteral(List<DatePatternToken> tokens, StringBuilder literal)
        {
            if (literal.Length > 0)
            {
                tokens.Add(Literal(literal.ToString()));
                _ = literal.Clear();
            }
        }

        /// <summary>A literal token.</summary>
        /// <param name="text">Its text.</param>
        /// <returns>The token.</returns>
        private static DatePatternToken Literal(string text)
        {
            return new DatePatternToken(DatePatternTokenKind.Literal, 0, text);
        }

        /// <summary>Whether a token is a literal of punctuation and spaces only, the kind that joins a weekday to a date.</summary>
        /// <param name="token">The token.</param>
        /// <returns>True when it is.</returns>
        private static bool IsJoiner(DatePatternToken token)
        {
            return token.Kind == DatePatternTokenKind.Literal
                && token.Text.All(static c => char.IsWhiteSpace(c) || char.IsPunctuation(c));
        }

        /// <summary>
        ///     Cleans and merges adjacent literals, collapses runs of spaces, and trims the spaces a
        ///     removed era or weekday left at either end.
        /// </summary>
        /// <param name="tokens">The tokens.</param>
        /// <returns>The normalised tokens.</returns>
        private static ImmutableArray<DatePatternToken> Normalise(IEnumerable<DatePatternToken> tokens)
        {
            // --- Merge adjacent literals, cleaning their text ---
            List<DatePatternToken> merged = [];
            foreach (DatePatternToken token in tokens)
            {
                if (token.Kind != DatePatternTokenKind.Literal)
                {
                    merged.Add(token);
                    continue;
                }

                string text = CalendarText.Clean(token.Text);
                if (merged.Count > 0 && merged[^1].Kind == DatePatternTokenKind.Literal)
                {
                    merged[^1] = Literal(merged[^1].Text + text);
                }
                else
                {
                    merged.Add(Literal(text));
                }
            }

            // --- Collapse space runs; trim the ends of the pattern ---
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Kind != DatePatternTokenKind.Literal)
                {
                    continue;
                }

                string text = CollapseSpaces(merged[i].Text);
                if (i == 0)
                {
                    text = text.TrimStart();
                }

                if (i == merged.Count - 1)
                {
                    text = text.TrimEnd();
                }

                merged[i] = Literal(text);
            }

            return [.. merged.Where(static t => t.Kind != DatePatternTokenKind.Literal || t.Text.Length > 0)];
        }

        /// <summary>Collapses every run of spaces to one space.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The collapsed text.</returns>
        private static string CollapseSpaces(string text)
        {
            if (!text.Contains("  ", StringComparison.Ordinal))
            {
                return text;
            }

            StringBuilder builder = new(text.Length);
            foreach (char c in text)
            {
                if (c == ' ' && builder.Length > 0 && builder[^1] == ' ')
                {
                    continue;
                }

                _ = builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
