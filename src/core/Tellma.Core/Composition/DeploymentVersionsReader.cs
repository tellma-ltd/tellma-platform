// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>Reads <see cref="DeploymentVersions" /> from the platform runtime and the distribution's assemblies.</summary>
    internal static class DeploymentVersionsReader
    {
        /// <summary>
        ///     The platform version is this assembly's informational version; the distribution
        ///     version is the entry assembly's, or, when the process has no managed entry assembly,
        ///     that of the assembly declaring the composition method.
        /// </summary>
        /// <param name="compose">The distribution's composition method.</param>
        /// <returns>The versions.</returns>
        public static DeploymentVersions Read(Delegate compose)
        {
            Assembly platform = typeof(DeploymentVersionsReader).Assembly;
            Assembly distribution = Assembly.GetEntryAssembly() ?? compose.Method.DeclaringType?.Assembly ?? platform;
            return new DeploymentVersions(InformationalVersion(platform), InformationalVersion(distribution));
        }

        /// <summary>
        ///     An assembly's informational version, which carries <c>+&lt;sha&gt;</c> under source
        ///     link; else its assembly version; else <c>0.0.0</c>.
        /// </summary>
        /// <param name="assembly">The assembly.</param>
        /// <returns>The version text.</returns>
        public static string InformationalVersion(Assembly assembly)
        {
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return !string.IsNullOrWhiteSpace(informational)
                ? informational
                : assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
    }
}
