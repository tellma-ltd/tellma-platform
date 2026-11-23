// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Reports
{
    // The reports' stack service's constructor is public, so it names only public types; the
    // platform's internal collaborators reach it through this one public bundle, which only the
    // platform can build, as the roles' stack does.
    /// <summary>
    ///     What the reports' stack service needs from the platform beyond its pipeline: the renderer
    ///     of default titles and the prefixes of the codes reserved for seeds. One per process.
    /// </summary>
    public sealed class ReportServiceDependencies
    {
        /// <summary>Gathers the collaborators.</summary>
        /// <param name="titles">The renderer of default titles.</param>
        /// <param name="reservedPrefixes">The prefixes of the codes reserved for seeds: each composed feature's name and a dot.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal ReportServiceDependencies(ReportTitles titles, IReadOnlyList<string> reservedPrefixes)
        {
            Titles = titles ?? throw new ArgumentNullException(nameof(titles));
            ReservedPrefixes = reservedPrefixes ?? throw new ArgumentNullException(nameof(reservedPrefixes));
        }

        /// <summary>The renderer of default titles.</summary>
        internal ReportTitles Titles { get; }

        /// <summary>The prefixes of the codes reserved for seeds, such as <c>core.</c> and <c>acme.</c>.</summary>
        internal IReadOnlyList<string> ReservedPrefixes { get; }
    }
}
