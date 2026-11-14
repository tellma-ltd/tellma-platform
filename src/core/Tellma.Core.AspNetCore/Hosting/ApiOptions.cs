// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The web API's defaults, bound from <c>Tellma:Api</c>: values a distribution never needs to
    ///     set.
    /// </summary>
    public sealed class ApiOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Api";

        /// <summary>
        ///     The client names the <c>Tellma-Client</c> header may carry; <c>web</c> by default. A
        ///     closed set: the name is the request context's client and a metric tag.
        /// </summary>
        public IReadOnlySet<string> ClientNames { get; set; } = new HashSet<string>(StringComparer.Ordinal) { "web" };

        /// <summary>The base every problem's <c>type</c> URI is formed from, ending in <c>/</c>.</summary>
        public string ProblemTypeBase { get; set; } = "https://tellma.com/problems/";

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The client names: a closed set of lowercase words ---
            if (ClientNames is null || ClientNames.Count == 0)
            {
                yield return new ValidationResult($"{SectionPath}:{nameof(ClientNames)} is empty; the SPA's name 'web' at least is required.", [nameof(ClientNames)]);
            }
            else
            {
                foreach (string name in ClientNames)
                {
                    if (!IsClientName(name))
                    {
                        yield return new ValidationResult(
                            $"{SectionPath}:{nameof(ClientNames)} holds '{name}'; a client name is 1 to 32 lowercase letters, digits or hyphens.",
                            [nameof(ClientNames)]);
                    }
                }
            }

            // --- The problem type base: an absolute URI every code is appended to ---
            if (string.IsNullOrWhiteSpace(ProblemTypeBase) || !ProblemTypeBase.EndsWith('/')
                || !Uri.TryCreate(ProblemTypeBase, UriKind.Absolute, out _))
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(ProblemTypeBase)} is '{ProblemTypeBase}'; it must be an absolute URI ending in '/'.",
                    [nameof(ProblemTypeBase)]);
            }
        }

        /// <summary>Whether a value is a well-formed client name.</summary>
        /// <param name="name">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsClientName(string? name)
        {
            return name is { Length: > 0 and <= 32 } && name.All(static c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
        }
    }
}
