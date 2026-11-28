// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     What the tenancy runtime knows of the composition it runs in: the distribution slug and
    ///     whether the host runs in Development. Taken from the composition rather than the host's
    ///     services, so it is the same in every host shape.
    /// </summary>
    /// <param name="Slug">The distribution slug.</param>
    /// <param name="IsDevelopment">Whether the host runs in Development.</param>
    internal sealed record TenancyEnvironment(string Slug, bool IsDevelopment)
    {
        /// <summary>
        ///     The migrator command that creates and migrates the catalog, as a sentence for a startup
        ///     failure or a log line: the distribution migrator's <c>migrate</c>, with the source
        ///     checkout's <c>dotnet run</c> form.
        /// </summary>
        public string MigrateCommand
        {
            get
            {
                // The reference layout: distributions/<slug>/src/Tellma.Distro.<Slug>.Migrator, the
                // slug's hyphen-separated words capitalised.
                string pascal = string.Concat(Slug.Split('-').Select(static word =>
                    word.Length == 0 ? word : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..]));

                return $"Run the distribution's migrator with the 'migrate' command; from a source checkout: dotnet run --project distributions/{Slug}/src/Tellma.Distro.{pascal}.Migrator -- migrate";
            }
        }
    }
}
