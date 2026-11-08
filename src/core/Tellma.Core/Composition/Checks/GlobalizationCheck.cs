// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.globalization</c>: the process runs ICU. Invariant globalization and the Windows NLS
    ///     mode both break the platform's time-zone conversion and calendar catalogue, which need ICU's
    ///     data.
    /// </summary>
    /// <param name="isRunningIcu">Reports whether the process runs ICU; the suites substitute it.</param>
    internal sealed class GlobalizationCheck(Func<bool> isRunningIcu) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Globalization;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            IReadOnlyList<CompositionProblem> problems = isRunningIcu()
                ? []
                : [new CompositionProblem(
                    Name,
                    "The process does not run ICU globalization: it runs invariant globalization or the Windows NLS mode.",
                    "Remove InvariantGlobalization and System.Globalization.UseNls from the project and runtimeconfig, unset DOTNET_SYSTEM_GLOBALIZATION_INVARIANT and DOTNET_SYSTEM_GLOBALIZATION_USENLS, and install ICU in the image (libicu, or icu-libs on Alpine).")];

            return Task.FromResult(problems);
        }
    }

    /// <summary>Detects whether the process runs ICU globalization.</summary>
    internal static class IcuProbe
    {
        /// <summary>
        ///     True when culture data comes from ICU. Under ICU the sort version's id packs the full
        ///     version into its first four bytes; NLS sort versions do not, and invariant mode either
        ///     refuses the culture or reports a zero version.
        /// </summary>
        /// <returns>Whether the process runs ICU.</returns>
        public static bool IsRunningIcu()
        {
            try
            {
                SortVersion version = new CultureInfo("en-US").CompareInfo.Version;
                byte[] id = version.SortId.ToByteArray();
                int packed = (id[3] << 24) | (id[2] << 16) | (id[1] << 8) | id[0];
                return packed != 0 && packed == version.FullVersion;
            }
            catch (CultureNotFoundException)
            {
                // Invariant mode with predefined cultures only refuses every named culture.
                return false;
            }
        }
    }
}
