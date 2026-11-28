// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text;
using Tellma.Core.Crud;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Crud
{
    /// <summary>
    ///     The descriptors the stack registry derives, pinned as text: the platform's and the fixture
    ///     schema's stacks with their default services, and the gadget scenario with a service,
    ///     a companion, every component kind and a routed service.
    /// </summary>
    /// <remarks>
    ///     A golden that no longer matches fails with the difference. After an intended change, run
    ///     the suite once with <c>TELLMA_UPDATE_GOLDENS=1</c>: the run rewrites the goldens and fails
    ///     on purpose, and the next run passes once the diff is reviewed.
    /// </remarks>
    public class StackDescriptorGoldenTests
    {
        private const string UpdateVariable = "TELLMA_UPDATE_GOLDENS";

        [Fact]
        public async Task The_platform_and_fixture_stacks_derive_their_descriptors_from_the_entities()
        {
            ServiceCollection services = [];
            _ = FixtureComposition.Compose(services);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            StackRegistry registry = provider.GetRequiredService<StackRegistry>();

            Assert.Empty(registry.Problems);
            Golden("fixture-stacks.txt", StackDescriptorText.Render(registry));
        }

        [Fact]
        public async Task A_stack_with_a_service_a_companion_and_components_derives_every_member()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            StackRegistry registry = provider.GetRequiredService<StackRegistry>();

            Assert.Empty(registry.Problems);
            Golden("gadget-stacks.txt", StackDescriptorText.Render(registry));
        }

        /// <summary>Compares a rendering with its golden, or rewrites the golden when asked to.</summary>
        /// <param name="name">The golden's file name.</param>
        /// <param name="rendered">The rendering.</param>
        private static void Golden(string name, string rendered)
        {
            string path = Path.Combine(GoldenDirectory(), name);
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                Assert.Fail("The stack descriptor golden was rewritten; review the diff and run again without " + UpdateVariable + " set.");
            }

            Assert.True(File.Exists(path), "No stack descriptor golden " + name + ". Set " + UpdateVariable + " to record one.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), rendered);
        }

        /// <summary>The directory of the goldens, beside this source file.</summary>
        /// <param name="here">This file's path, supplied by the compiler.</param>
        /// <returns>The directory.</returns>
        private static string GoldenDirectory([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here)!, "Goldens");
        }
    }
}
