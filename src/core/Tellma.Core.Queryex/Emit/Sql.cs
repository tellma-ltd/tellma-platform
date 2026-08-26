// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Queryex.Emit
{
    /// <summary>
    ///     Builds emission strategies from patterns.
    /// </summary>
    /// <remarks>
    ///     One of only two places in the engine that author T-SQL text; the other is the writer that
    ///     assembles a statement. A second backend dialect would supply its own parallel set.
    /// </remarks>
    internal static class Sql
    {
        /// <summary>A template producing a scalar.</summary>
        /// <param name="pattern">The emission pattern.</param>
        /// <returns>The strategy.</returns>
        internal static EmitStrategy Value(string pattern)
        {
            return new TemplateStrategy(EmitTemplate.Parse(pattern), EmitShape.Value);
        }

        /// <summary>A value whose pattern fixes the type the backend holds it in.</summary>
        /// <param name="pattern">The emission pattern.</param>
        /// <param name="result">The type the result is held in.</param>
        /// <returns>The strategy.</returns>
        /// <remarks>
        ///     Stated wherever the SQL produces a whole number, because a total over one has to be
        ///     widened before it is taken and a division of one has to be widened before it divides.
        ///     Both go wrong quietly otherwise.
        /// </remarks>
        internal static EmitStrategy Value(string pattern, QueryexStoreType result)
        {
            return new TemplateStrategy(EmitTemplate.Parse(pattern, result), EmitShape.Value);
        }

        /// <summary>A value held in whatever type one of its arguments is held in.</summary>
        /// <param name="pattern">The emission pattern.</param>
        /// <param name="argument">The argument whose type the result takes.</param>
        /// <returns>The strategy.</returns>
        internal static EmitStrategy ValueLike(string pattern, int argument)
        {
            return new TemplateStrategy(
                EmitTemplate.Parse(pattern, result: null, follows: argument),
                EmitShape.Value);
        }

        /// <summary>A template producing a truth value.</summary>
        /// <param name="pattern">The emission pattern.</param>
        /// <returns>The strategy.</returns>
        internal static EmitStrategy Predicate(string pattern)
        {
            return new TemplateStrategy(EmitTemplate.Parse(pattern), EmitShape.Predicate);
        }

        /// <summary>One of several templates, chosen by a literal selector argument.</summary>
        /// <param name="selectorIndex">Which argument chooses.</param>
        /// <param name="cases">The patterns, by the selector value that chooses each.</param>
        /// <returns>The strategy.</returns>
        internal static EmitStrategy Selected(int selectorIndex, params (string Value, string Pattern)[] cases)
        {
            return Selected(selectorIndex, null, cases);
        }

        /// <summary>One of several templates, all of which fix the same result type.</summary>
        /// <param name="selectorIndex">Which argument chooses.</param>
        /// <param name="result">The type the result is held in.</param>
        /// <param name="cases">The patterns, by the selector value that chooses each.</param>
        /// <returns>The strategy.</returns>
        internal static EmitStrategy Selected(
            int selectorIndex,
            QueryexStoreType? result,
            params (string Value, string Pattern)[] cases)
        {
            Dictionary<string, EmitTemplate> templates = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string value, string pattern) in cases)
            {
                templates[value] = EmitTemplate.Parse(pattern, result);
            }

            return new SelectedStrategy(
                selectorIndex,
                templates.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                EmitShape.Value);
        }

        /// <summary>A value the host supplies at execution.</summary>
        /// <param name="origin">Where the value comes from.</param>
        /// <returns>The strategy.</returns>
        internal static EmitStrategy Context(QueryexParameterOrigin origin)
        {
            return new ContextStrategy(origin);
        }
    }
}
