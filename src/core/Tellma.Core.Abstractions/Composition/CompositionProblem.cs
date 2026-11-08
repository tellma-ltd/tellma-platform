// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     One problem found while composing or starting a host: where it was found, what is wrong
    ///     and how to fix it.
    /// </summary>
    /// <remarks>
    ///     <see cref="Source" /> and <see cref="Problem" /> are redeclared get-only, so a
    ///     <c>with</c> expression cannot blank what the constructor validated.
    /// </remarks>
    /// <param name="Source">Where the problem was found: a startup check's name, such as
    ///     <c>core.options</c>, or <c>core.composition</c> for the composition itself.</param>
    /// <param name="Problem">What is wrong, as one sentence that names the offending feature, type,
    ///     key or value.</param>
    /// <param name="Fix">The remedy, phrased as the code or configuration to add; null only when no
    ///     remedy can be stated.</param>
    public sealed record CompositionProblem(string Source, string Problem, string? Fix)
    {
        /// <inheritdoc cref="CompositionProblem(string, string, string?)" />
        public string Source { get; } = ValidateText(Source, nameof(Source));

        /// <inheritdoc cref="CompositionProblem(string, string, string?)" />
        public string Problem { get; } = ValidateText(Problem, nameof(Problem));

        /// <summary>
        ///     Renders the problem as one line: the source, the problem and, when present, the fix.
        ///     Line breaks inside any component (an embedded exception message can carry several) are
        ///     rendered as spaces, so the rendering is always exactly one line.
        /// </summary>
        /// <returns>The one-line rendering.</returns>
        public string Describe()
        {
            string line = Fix is null ? $"[{Source}] {Problem}" : $"[{Source}] {Problem} Fix: {Fix}";
            return line.ReplaceLineEndings(" ");
        }

        /// <summary>Refuses a null, empty or whitespace component.</summary>
        /// <param name="value">The component.</param>
        /// <param name="parameterName">The component's parameter name.</param>
        /// <returns><paramref name="value" />.</returns>
        private static string ValidateText(string value, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
            return value;
        }
    }
}
