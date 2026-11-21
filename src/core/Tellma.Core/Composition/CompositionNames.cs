// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Composition
{
    /// <summary>The grammar of distribution slugs and feature names, and the composition's problem source.</summary>
    internal static class CompositionNames
    {
        /// <summary>The <see cref="Abstractions.Composition.CompositionProblem.Source" /> of every problem the composition root finds itself.</summary>
        public const string Source = "core.composition";

        /// <summary>The inclusive upper bound on a slug's length, set by the tightest Azure resource names.</summary>
        public const int MaxSlugLength = 15;

        /// <summary>
        ///     Whether <paramref name="slug" /> is a distribution slug: 1 to 15 lowercase letters,
        ///     digits or hyphens, starting with a letter.
        /// </summary>
        /// <param name="slug">The candidate slug.</param>
        /// <returns>True when the slug is valid.</returns>
        public static bool IsValidSlug(string? slug)
        {
            if (string.IsNullOrEmpty(slug) || slug.Length > MaxSlugLength || slug[0] is not (>= 'a' and <= 'z'))
            {
                return false;
            }

            foreach (char c in slug)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Whether <paramref name="name" /> is a feature name: dot-separated segments of lowercase
        ///     letters, digits and hyphens, each starting with a letter (<c>core</c>, <c>gl</c>,
        ///     <c>acme.sales</c>).
        /// </summary>
        /// <param name="name">The candidate name.</param>
        /// <returns>True when the name is valid.</returns>
        public static bool IsValidFeatureName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // A distribution's own feature is typically named after its slug, so a segment takes the
            // slug's alphabet; the dot is reserved as the separator of the step and key grammars.
            foreach (string segment in name.Split('.'))
            {
                if (segment.Length == 0 || segment[0] is not (>= 'a' and <= 'z'))
                {
                    return false;
                }

                foreach (char c in segment)
                {
                    if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
