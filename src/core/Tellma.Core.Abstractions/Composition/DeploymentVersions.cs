// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     The build of the running deployment: the informational versions of the platform runtime and
    ///     of the distribution. Registered once by the composition root as a singleton; the information
    ///     document, the log enricher and the trace resource read it.
    /// </summary>
    /// <remarks>
    ///     Both versions carry the source commit as <c>+&lt;sha&gt;</c> when the assemblies are built
    ///     from a repository with source link, so any log line or trace names the exact commits.
    ///     Components are redeclared get-only, so a <c>with</c> expression cannot blank them.
    /// </remarks>
    /// <param name="PlatformVersion">The informational version of the platform runtime assembly.</param>
    /// <param name="DistributionVersion">The informational version of the distribution's entry assembly.</param>
    public sealed record DeploymentVersions(string PlatformVersion, string DistributionVersion)
    {
        /// <inheritdoc cref="DeploymentVersions(string, string)" />
        public string PlatformVersion { get; } = ValidateVersion(PlatformVersion, nameof(PlatformVersion));

        /// <inheritdoc cref="DeploymentVersions(string, string)" />
        public string DistributionVersion { get; } = ValidateVersion(DistributionVersion, nameof(DistributionVersion));

        /// <summary>Refuses a null, empty or whitespace version.</summary>
        /// <param name="value">The version.</param>
        /// <param name="parameterName">The version's parameter name.</param>
        /// <returns><paramref name="value" />.</returns>
        private static string ValidateVersion(string value, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
            return value;
        }
    }
}
