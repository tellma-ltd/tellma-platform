// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Reflection.Emit;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>
    ///     The deployment's versions: the platform's is the runtime's, and the distribution's is its
    ///     composition library's, whichever process composes it.
    /// </summary>
    public class DeploymentVersionsReaderTests
    {
        [Fact]
        public void The_distribution_version_is_the_composition_library_s_not_the_entry_assembly_s()
        {
            // A method declared in an assembly whose version is not the entry assembly's (the test
            // host here, as the web host or the migrator is in production) stands in for the
            // distribution's composition method.
            Func<int, int> compose = Math.Abs;
            string library = DeploymentVersionsReader.InformationalVersion(typeof(Math).Assembly);
            Assert.NotEqual(library, DeploymentVersionsReader.InformationalVersion(Assembly.GetEntryAssembly()!));

            DeploymentVersions versions = DeploymentVersionsReader.Read(compose);

            Assert.Equal(library, versions.DistributionVersion);
            Assert.Equal(DeploymentVersionsReader.InformationalVersion(typeof(CoreFeature).Assembly), versions.PlatformVersion);
        }

        [Fact]
        public void A_method_with_no_declaring_type_falls_back_to_the_entry_assembly()
        {
            // A dynamic method belongs to no type, so no composition library can be read from it.
            DynamicMethod method = new("Compose", typeof(void), [typeof(TellmaBuilder)]);
            method.GetILGenerator().Emit(OpCodes.Ret);
            Delegate compose = method.CreateDelegate<Action<TellmaBuilder>>();

            DeploymentVersions versions = DeploymentVersionsReader.Read(compose);

            Assert.Equal(DeploymentVersionsReader.InformationalVersion(Assembly.GetEntryAssembly()!), versions.DistributionVersion);
        }
    }
}
