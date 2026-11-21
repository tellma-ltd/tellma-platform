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
    /// <param name="DistributionVersion">The informational version of the distribution's composition library, the assembly declaring its composition method.</param>
    public sealed record DeploymentVersions(string PlatformVersion, string DistributionVersion)
    {
        /// <summary>
        ///     The longest version a client states in its <c>Tellma-Client</c> header, which keeps
        ///     the logged value bounded: room for any SemVer version with a pre-release label and
        ///     build metadata that carries a full commit sha.
        /// </summary>
        public const int MaxClientVersionLength = 128;

        /// <summary>
        ///     Whether a version can be stated by a client in its <c>Tellma-Client</c> header: 1 to
        ///     <see cref="MaxClientVersionLength" /> visible ASCII characters, no space and no
        ///     control character. The web client states the distribution's version, so a
        ///     composition whose distribution version is not one is refused.
        /// </summary>
        /// <param name="version">The version.</param>
        /// <returns>True when it is.</returns>
        public static bool IsClientVersion(ReadOnlySpan<char> version)
        {
            if (version.Length is 0 or > MaxClientVersionLength)
            {
                return false;
            }

            foreach (char c in version)
            {
                if (c is < '!' or > '~')
                {
                    return false;
                }
            }

            return true;
        }

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
