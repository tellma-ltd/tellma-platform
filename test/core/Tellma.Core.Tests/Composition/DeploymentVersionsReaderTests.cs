// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Reflection.Emit;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>
    ///     The deployment's versions: the platform's is the runtime's, and the distribution's is its
    ///     composition library's, whichever process composes it; a distribution version the web
    ///     client cannot state in its header is refused at composition.
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

        [Fact]
        public void The_platform_s_informational_version_with_its_commit_is_a_client_version()
        {
            string version = DeploymentVersionsReader.InformationalVersion(typeof(CoreFeature).Assembly);

            Assert.True(DeploymentVersions.IsClientVersion(version), version);
        }

        [Theory]
        [InlineData("1.0.0", true)]
        [InlineData("1.12.0-preview.20261120.12+0123456789abcdef0123456789abcdef01234567", true)]
        [InlineData("1.4.0+Branch.develop.Sha.0123456789abcdef0123456789abcdef01234567", true)]
        [InlineData("1.0.0 (beta)", false)]
        [InlineData("", false)]
        public void A_client_version_is_bounded_visible_ascii(string version, bool expected)
        {
            Assert.Equal(expected, DeploymentVersions.IsClientVersion(version));
            Assert.False(DeploymentVersions.IsClientVersion(new string('1', DeploymentVersions.MaxClientVersionLength + 1)));
            Assert.True(DeploymentVersions.IsClientVersion(new string('1', DeploymentVersions.MaxClientVersionLength)));
        }

        [Fact]
        public void A_distribution_version_the_web_client_cannot_state_fails_composition()
        {
            // A host registers its composition's versions before composing, as the web host does.
            ServiceCollection services = new();
            string version = "1.0.0+" + new string('a', DeploymentVersions.MaxClientVersionLength);
            _ = services.AddSingleton(new DeploymentVersions("1.0.0", version));

            TellmaCompositionException exception = Assert.Throws<TellmaCompositionException>(() => TestComposition.Compose(static _ => { }, services: services));

            CompositionProblem problem = Assert.Single(exception.Problems);
            Assert.Contains($"'{version}'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Tellma-Client", problem.Problem, StringComparison.Ordinal);
        }
    }
}
