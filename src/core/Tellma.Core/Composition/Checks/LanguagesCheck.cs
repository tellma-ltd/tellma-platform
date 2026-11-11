// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Localization;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.languages</c>: every declared language is in the catalogue or was added, a
    ///     declared region is one of its language's, an added language is well formed, symbols are
    ///     unique, and the platform's strings ship for every declared language but English.
    /// </summary>
    /// <remarks>
    ///     Reads the composition and the assemblies beside it, so a build step without a deployment
    ///     runs it too. The catalogue resolves a declaration with mistakes leniently so that this
    ///     check, and not a failing constructor, is where a mistake surfaces.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    /// <param name="hasCoreSatellite">Reports whether the platform's strings ship for a language; the suites substitute it.</param>
    internal sealed class LanguagesCheck(TellmaCompositionRecord record, Func<string, bool> hasCoreSatellite) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Languages;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <summary>Whether this assembly's satellite for a language is beside it.</summary>
        /// <param name="language">The language code.</param>
        /// <returns>True when the satellite assembly loads.</returns>
        public static bool HasCoreSatellite(string language)
        {
            try
            {
                _ = typeof(LanguagesCheck).Assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo(language));
                return true;
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException or CultureNotFoundException)
            {
                // Absent, unreadable, or a language this host cannot even name: not shipped.
                return false;
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            // --- The declaration against the catalogue ---
            var set = LanguageSet.Resolve(record.Languages);
            List<CompositionProblem> problems = [.. set.Problems.Select(problem => new CompositionProblem(Name, problem.Problem, problem.Fix))];

            // --- The platform's strings, for every offered language but English ---
            // English is the neutral language of the platform's resources and needs no satellite.
            foreach (LanguageInfo language in set.Offered)
            {
                if (!string.Equals(language.Code, LanguageSet.English, StringComparison.OrdinalIgnoreCase) && !hasCoreSatellite(language.Code))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The distribution offers the language '{language.Code}', but the Tellma.Core satellite assembly for it ({language.Code}/Tellma.Core.resources.dll) is not deployed.",
                        $"If the distribution's project sets SatelliteResourceLanguages, include '{language.Code}' in it. If the platform ships no '{language.Code}' strings, stop offering the language: remove '{language.Code}' from tellma.Languages(...)."));
                }
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
