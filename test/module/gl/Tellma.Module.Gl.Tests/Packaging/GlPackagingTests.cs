// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyModel;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Module.Gl.Tests.Packaging
{
    /// <summary>
    ///     What the GL packages may reach and what the feature contributes: neither package's
    ///     dependency closure holds Tellma.Core or an Entity Framework package, and the feature is
    ///     named for the module, requires nothing and contributes exactly its stack, its provisioning
    ///     step and its default sample centers.
    /// </summary>
    public class GlPackagingTests
    {
        [Theory]
        [InlineData("Tellma.Module.Gl")]
        [InlineData("Tellma.Module.Gl.Abstractions")]
        public void A_packages_dependency_closure_holds_no_runtime_and_no_entity_framework(string package)
        {
            // A framework-dependent publish ships exactly the closure, so this is what a host
            // referencing the package receives. The walk starts at the package's own node of this
            // test app's deps.json, so the suite's own references (the runtime among them) do not count.
            HashSet<string> closure = Closure(package);

            // Sanity: the walk found the real graph.
            Assert.Contains("Tellma.Core.Abstractions", closure);

            Assert.DoesNotContain("Tellma.Core", closure);
            Assert.DoesNotContain(closure, static name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(closure, static name => name.StartsWith("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(closure, static name => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void The_contract_package_references_nothing_but_the_platforms_contract()
        {
            Assert.Equal(
                ["Tellma.Core.Abstractions", "Tellma.Core.Queryex", "Tellma.Module.Gl.Abstractions"],
                Closure("Tellma.Module.Gl.Abstractions").Order(StringComparer.Ordinal));
        }

        [Fact]
        public void Neither_assembly_references_the_runtime()
        {
            Assert.All(
                [typeof(GlFeature).Assembly, typeof(Center).Assembly],
                static assembly => Assert.DoesNotContain(assembly.GetReferencedAssemblies(), static name => name.Name == "Tellma.Core"));
        }

        [Fact]
        public void The_feature_is_named_for_the_module_and_requires_nothing()
        {
            GlFeature feature = new();
            FeatureDeclaration declaration = new();

            feature.Declare(declaration);

            Assert.Equal("gl", GlModule.FeatureName);
            Assert.Equal(GlModule.FeatureName, feature.Name);
            Assert.Empty(declaration.RequiredFeatures);
            Assert.Empty(declaration.OptionsBindings);
        }

        [Fact]
        public void The_feature_contributes_its_stack_its_step_and_its_default_sample_centers_alone()
        {
            FeatureContribution contribution = new();

            new GlFeature().Contribute(contribution);

            Assert.Empty(contribution.Features);
            Assert.Collection(
                contribution.Items,
                static item => Assert.Equal(new StackContributionItem(typeof(Center), typeof(CenterService<Center>), null), item),
                static item => Assert.Equal(new ProvisioningStepContributionItem(typeof(GlSampleCentersStep<Center>)), item),
                static item =>
                {
                    ServiceContributionItem service = Assert.IsType<ServiceContributionItem>(item);
                    Assert.Equal((typeof(IGlSampleCenters), typeof(DefaultGlSampleCenters), ServiceLifetimeKind.Singleton), (service.ServiceType, service.ImplementationType, service.Lifetime));
                });
        }

        /// <summary>The names of the libraries a package's node of this test app's deps.json reaches, itself included.</summary>
        /// <param name="package">The package.</param>
        /// <returns>The closure.</returns>
        private static HashSet<string> Closure(string package)
        {
            DependencyContext context = DependencyContext.Default!;
            var libraries = context.RuntimeLibraries.ToDictionary(static library => library.Name, StringComparer.OrdinalIgnoreCase);
            Assert.True(libraries.ContainsKey(package), $"The test app's deps.json has no node for {package}.");

            // --- Breadth first over the dependency edges ---
            HashSet<string> closure = new(StringComparer.OrdinalIgnoreCase);
            Queue<string> pending = new([package]);
            while (pending.TryDequeue(out string? name))
            {
                if (!closure.Add(name) || !libraries.TryGetValue(name, out RuntimeLibrary? library))
                {
                    continue;
                }

                foreach (Dependency dependency in library.Dependencies)
                {
                    pending.Enqueue(dependency.Name);
                }
            }

            return closure;
        }
    }
}
