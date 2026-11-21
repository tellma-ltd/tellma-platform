// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Restricts a query to the rows whose key is held in a table the host supplies.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         How a host narrows a query to keys it already holds — the rows captured by an earlier
    ///         statement, or the ids a caller sent — without writing their values into expression
    ///         text. Each restriction is one more conjunct of the statement's row filter, after the
    ///         filter itself, of the form <c>&lt;column&gt; IN (SELECT [Id] FROM &lt;table&gt;)</c>.
    ///     </para>
    ///     <para>
    ///         The conjunct is never negated, so a row whose key is absent never matches. A
    ///         restriction applies to the rows of the query's root only, and in a grouped query it
    ///         filters the rows that enter the groups, as the filter does.
    ///     </para>
    ///     <para>
    ///         The table name is host-authored text that reaches the emitted SQL as written, beside
    ///         the schema's sources and columns. It must never be derived from anything a user
    ///         supplied.
    ///     </para>
    /// </remarks>
    /// <param name="Path">
    ///     A dotted path from the query's root to an equatable scalar property, such as <c>Id</c>,
    ///     <c>ParentId</c> or <c>Invoice.Id</c>. Resolved like a path in a select list, so the
    ///     navigations it crosses share the joins every other clause uses.
    /// </param>
    /// <param name="TableSource">
    ///     The table holding the keys, written as <c>@</c> followed by a letter or underscore and
    ///     then letters, digits or underscores: a table-valued parameter the host binds, or a table
    ///     variable it declares. The table has an <c>[Id]</c> column of the key's type. Names in the
    ///     engine's reserved <c>@qx</c> namespace are refused.
    /// </param>
    public sealed record KeySetRestriction(string Path, string TableSource)
    {
        /// <summary>The longest name a backend variable may have, its leading marker included.</summary>
        private const int MaxTableSourceLength = 128;

        /// <summary>The path from the root to the restricted property.</summary>
        /// <exception cref="ArgumentException">The path is null, empty, or has an empty segment.</exception>
        /// <remarks>
        ///     Only its form is checked here. Whether it resolves depends on the schema and root it is
        ///     compiled against, so that is checked when the query is compiled.
        /// </remarks>
        public string Path
        {
            get;
            init => field = RequirePath(value, nameof(value));
        } = RequirePath(Path, nameof(Path));

        /// <summary>The table holding the keys.</summary>
        /// <exception cref="ArgumentException">
        ///     The name is not of the accepted form, or lies in the engine's reserved namespace.
        /// </exception>
        public string TableSource
        {
            get;
            init => field = RequireTableSource(value, nameof(value));
        } = RequireTableSource(TableSource, nameof(TableSource));

        /// <summary>Checks a path's form.</summary>
        /// <param name="path">The path.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The same path.</returns>
        /// <exception cref="ArgumentException">The path is not usable.</exception>
        private static string RequirePath(string path, string parameterName)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A restriction must name the path it restricts.", parameterName);
            }

            foreach (string segment in path.Split('.'))
            {
                if (segment.Length == 0)
                {
                    throw new ArgumentException(
                        "A restriction's path must not contain an empty segment.",
                        parameterName);
                }
            }

            return path;
        }

        /// <summary>Checks a table source's form.</summary>
        /// <param name="source">The table source.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The same table source.</returns>
        /// <exception cref="ArgumentException">The table source is not usable.</exception>
        /// <remarks>
        ///     The form is closed so that the name can reach SQL as written: a marker, then an
        ///     identifier with no character that could end it. The reserved namespace is refused
        ///     ignoring case, because the backend matches variable names that way and an engine name
        ///     differing only in case would still collide.
        /// </remarks>
        private static string RequireTableSource(string source, string parameterName)
        {
            return !IsTableSource(source)
                ? throw new ArgumentException(
                    "A restriction's table must be named as '@' followed by a letter or underscore and then letters, digits or underscores.",
                    parameterName)
                : source.StartsWith("@qx", StringComparison.OrdinalIgnoreCase)
                    ? throw new ArgumentException(
                        "A restriction's table must not be named in the engine's reserved '@qx' namespace.",
                        parameterName)
                    : source;
        }

        /// <summary>Whether a table source has the accepted form.</summary>
        /// <param name="source">The table source.</param>
        /// <returns>True when it does.</returns>
        private static bool IsTableSource(string? source)
        {
            if (source is null || source.Length < 2 || source.Length > MaxTableSourceLength || source[0] != '@')
            {
                return false;
            }

            if (!IsIdentifierStart(source[1]))
            {
                return false;
            }

            for (int index = 2; index < source.Length; index++)
            {
                if (!IsIdentifierStart(source[index]) && !char.IsAsciiDigit(source[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a character may start an identifier.</summary>
        /// <param name="character">The character.</param>
        /// <returns>True for an ASCII letter or an underscore.</returns>
        private static bool IsIdentifierStart(char character)
        {
            return char.IsAsciiLetter(character) || character == '_';
        }
    }
}
