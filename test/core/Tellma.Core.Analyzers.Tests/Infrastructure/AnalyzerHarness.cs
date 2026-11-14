// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;

namespace Tellma.Core.Analyzers.Tests.Infrastructure
{
    /// <summary>
    ///     Runs one analyzer over a test source, with the expected diagnostics written as markup in
    ///     the source (<c>{|TELLMA0003:text|}</c>), and fails on any diagnostic the markup does not
    ///     name, so every negative is pinned as silence.
    /// </summary>
    /// <remarks>
    ///     The compilation references the assemblies this suite runs on, the real contract among
    ///     them, and never downloads reference packs, so the suite runs offline. The runtime and its
    ///     siblings are left out so a test compilation may take the runtime's assembly name; the
    ///     types the rules need from it are declared in <see cref="PlatformStubs" />.
    /// </remarks>
    internal static class AnalyzerHarness
    {
        /// <summary>The assembly name a distribution's library would have.</summary>
        public const string DistributionAssembly = "Acme.Distribution";

        /// <summary>The assemblies of the platform a test compilation may reference: the contract and its one edge.</summary>
        private static readonly ImmutableHashSet<string> PlatformReferences = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "Tellma.Core.Abstractions",
            "Tellma.Core.Queryex");

        /// <summary>The metadata references of every test compilation, read once.</summary>
        private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

        /// <summary>Runs an analyzer over a source and checks its diagnostics against the source's markup.</summary>
        /// <typeparam name="TAnalyzer">The analyzer.</typeparam>
        /// <param name="source">The source, with markup.</param>
        /// <param name="assemblyName">The compilation's assembly name.</param>
        /// <returns>A task that completes when the check has passed.</returns>
        public static Task VerifyAsync<TAnalyzer>(string source, string assemblyName = DistributionAssembly)
            where TAnalyzer : DiagnosticAnalyzer, new()
        {
            CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> test = new()
            {
                // No reference pack: the references below are the running framework's own assemblies.
                ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
            };
            test.TestState.Sources.Add(("Test.cs", source));
            test.TestState.Sources.Add(("PlatformStubs.cs", PlatformStubs.Source));
            test.TestState.AdditionalReferences.AddRange(References.Value);
            test.SolutionTransforms.Add((solution, projectId) => solution.WithProjectAssemblyName(projectId, assemblyName));
            return test.RunAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Reads the running framework's assemblies, the platform's contract and the packages the
        ///     rules name (EF Core, SqlClient), leaving out the rest of the platform and this suite.
        /// </summary>
        /// <returns>The references.</returns>
        private static ImmutableArray<MetadataReference> LoadReferences()
        {
            string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>();
            foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                bool platform = name.StartsWith("Tellma.", StringComparison.OrdinalIgnoreCase);
                if (!platform || PlatformReferences.Contains(name))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }

            return references.ToImmutable();
        }
    }
}
