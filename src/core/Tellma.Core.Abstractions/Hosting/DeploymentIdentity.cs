// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Hosting
{
    /// <summary>
    ///     Identifies this running deployment across the fleet. Registered once at composition as a
    ///     singleton; consumed by any feature that must name the deployment durably or on a wire —
    ///     the email correlation envelope, export file naming, future connectors.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately not configuration: the application half is a compile-time constant of the
    ///         composition — an app knows its own name the way it knows its assembly name, and
    ///         configuration would let two deployments claim each other by mistake — while the
    ///         environment half comes from the host at startup. Pipelines that depend on it (email)
    ///         validate at startup that an instance is registered.
    ///     </para>
    ///     <para>
    ///         Both components are get-only so a <c>with</c> expression cannot leave
    ///         <see cref="DeploymentId" /> disagreeing with the values it was derived from.
    ///     </para>
    /// </remarks>
    /// <param name="Application">The deployable's hardcoded name: a distribution's slug
    ///     ("etpharma"), "identity" for the identity server. Lowercase letters, digits, and
    ///     hyphens; validated in the constructor.</param>
    /// <param name="EnvironmentName">The host environment name
    ///     (<c>IHostEnvironment.EnvironmentName</c>).</param>
    public sealed record DeploymentIdentity(string Application, string EnvironmentName)
    {
        /// <summary>The inclusive upper bound on the length of <see cref="Application" />.</summary>
        public const int MaxApplicationLength = 32;

        // Spelled out rather than taken from Microsoft.Extensions.Hosting's Environments class:
        // this assembly is a pure contract surface and carries no package references at all.
        private const string ProductionEnvironmentName = "Production";

        /// <inheritdoc cref="DeploymentIdentity(string, string)" />
        public string Application { get; } = ValidateApplication(Application);

        /// <inheritdoc cref="DeploymentIdentity(string, string)" />
        public string EnvironmentName { get; } = ValidateEnvironmentName(EnvironmentName);

        /// <summary>
        ///     The fleet-unique deployment id: <see cref="Application" /> alone in Production,
        ///     environment-qualified (lowercased) otherwise — "etpharma", "etpharma-staging",
        ///     "identity-development". Lowercase kebab-case and colon-free, safe for wire envelopes
        ///     and file names; validated in the constructor.
        /// </summary>
        public string DeploymentId { get; } = ComposeDeploymentId(Application, EnvironmentName);

        private static string ValidateApplication(string application)
        {
            ArgumentNullException.ThrowIfNull(application);
            return application.Length is > 0 and <= MaxApplicationLength && IsLowercaseKebabCase(application)
                ? application
                : throw new ArgumentException(
                    $"A deployment application name must be 1 to {MaxApplicationLength} lowercase letters, digits, or hyphens; received '{application}'.",
                    nameof(application));
        }

        private static string ValidateEnvironmentName(string environmentName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
            return environmentName;
        }

        private static string ComposeDeploymentId(string application, string environmentName)
        {
            // Production stays unqualified so the id reads as the slug everywhere ids surface;
            // every other environment is qualified, which is what keeps a staging deployment's wire
            // artifacts from ever colliding with production's.
            if (string.Equals(environmentName, ProductionEnvironmentName, StringComparison.OrdinalIgnoreCase))
            {
                return application;
            }

            string composed = $"{application}-{environmentName.ToLowerInvariant()}";

            // Sanitizing instead would let two environments silently collapse onto one id, and the
            // id is what keeps their wire artifacts apart.
            return IsLowercaseKebabCase(composed)
                ? composed
                : throw new ArgumentException(
                    $"The host environment name '{environmentName}' does not compose a valid deployment id: '{composed}' must contain only lowercase letters, digits, and hyphens.",
                    nameof(environmentName));
        }

        private static bool IsLowercaseKebabCase(ReadOnlySpan<char> value)
        {
            foreach (char c in value)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
