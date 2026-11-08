// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.public-origin</c>: <c>Tellma:PublicOrigin</c> is set to an absolute origin with no
    ///     path, query, fragment or user information, over https outside Development. Redirect URIs,
    ///     resource identifiers, the back-channel URI, server-built links and the cross-site request
    ///     origin check all derive from it.
    /// </summary>
    /// <param name="record">The realised composition.</param>
    internal sealed class PublicOriginCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <summary>The configuration key the check reads.</summary>
        public const string Key = "Tellma:PublicOrigin";

        private const string Example = "https://acme.app.tellma.com";

        /// <inheritdoc />
        public string Name => CoreCheckNames.PublicOrigin;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            string? problem = Describe(record.Configuration[Key], record.Environment.IsDevelopment());
            IReadOnlyList<CompositionProblem> problems = problem is null
                ? []
                : [new CompositionProblem(
                    Name,
                    problem,
                    $"Set {Key} to the origin users reach the distribution at, such as '{Example}': scheme, host and optional port only.")];

            return Task.FromResult(problems);
        }

        /// <summary>Describes what is wrong with a configured origin.</summary>
        /// <param name="value">The configured value.</param>
        /// <param name="isDevelopment">Whether the host runs in Development, where http is allowed.</param>
        /// <returns>The problem, or null for a valid origin.</returns>
        private static string? Describe(string? value, bool isDevelopment)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return $"{Key} is not set.";
            }

            // The scheme separator is required explicitly: on Linux a rooted path such as '/acme'
            // parses as an absolute file URI, and the check must read alike on every host.
            if (!value.Contains("://", StringComparison.Ordinal) || !Uri.TryCreate(value, UriKind.Absolute, out Uri? origin))
            {
                return $"{Key} is '{value}', which is not an absolute URI.";
            }

            if (origin.Scheme != Uri.UriSchemeHttps && !(isDevelopment && origin.Scheme == Uri.UriSchemeHttp))
            {
                return isDevelopment
                    ? $"{Key} is '{value}', which is neither https nor http."
                    : $"{Key} is '{value}', which is not https; only Development may serve over http.";
            }

            // The origin is concatenated into every derived URI, so even a trailing '/' would double
            // the separator in them.
            return origin.UserInfo.Length > 0 || origin.AbsolutePath != "/" || value.EndsWith('/')
                || origin.Query.Length > 0 || origin.Fragment.Length > 0
                ? $"{Key} is '{value}', which carries more than an origin: a path, a trailing slash, a query, a fragment or user information."
                : null;
        }
    }
}
