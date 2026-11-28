// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     Rows the operation names do not exist or are hidden from the caller by row-level
    ///     security; the two cases are identical, so a caller cannot probe for rows it may not see.
    ///     Maps to 404 with the problem code <c>not-found</c>.
    /// </summary>
    /// <remarks>
    ///     <see cref="TellmaException.Arguments" /> carries <c>resource</c> and <c>ids</c>. The ids
    ///     are strings: an exception carries no stack and so no key type, and the ids are display
    ///     data a client never matches to loaded rows.
    /// </remarks>
    /// <param name="resource">The entity name of the rows, such as <c>gl.Center</c>.</param>
    /// <param name="ids">The ids not found, as strings; empty when the operation named none.</param>
    /// <exception cref="ArgumentException"><paramref name="resource" /> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="ids" /> is null.</exception>
    public sealed class NotFoundException(string resource, IReadOnlyList<string> ids)
        : TellmaException(ProblemCode, Describe(resource, ids), ArgumentsOf(resource, ids))
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "not-found";

        /// <summary>The entity name of the rows, such as <c>gl.Center</c>.</summary>
        public string Resource { get; } = ValidateResource(resource);

        /// <summary>The ids not found, as strings.</summary>
        public IReadOnlyList<string> Ids { get; } = ReadOnlyCopies.OfEntries(ids, requireOne: false, nameof(ids));

        /// <summary>Refuses an empty resource.</summary>
        /// <param name="resource">The resource.</param>
        /// <returns>The resource.</returns>
        private static string ValidateResource(string resource)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            return resource;
        }

        /// <summary>Renders the log message.</summary>
        /// <param name="resource">The resource, already validated.</param>
        /// <param name="ids">The ids, already validated.</param>
        /// <returns>The message.</returns>
        private static string Describe(string resource, IReadOnlyList<string> ids)
        {
            return ids.Count == 0
                ? $"The {resource} was not found."
                : $"No visible {resource} with id {LogText.List(ids, static id => id)}.";
        }

        /// <summary>The display arguments: <c>resource</c> and <c>ids</c>.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="ids">The ids.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> ArgumentsOf(string resource, IReadOnlyList<string> ids)
        {
            return new(StringComparer.Ordinal) { ["resource"] = resource, ["ids"] = ReadOnlyCopies.Of(ids) };
        }
    }
}
