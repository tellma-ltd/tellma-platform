// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Tests.PublicApi
{
    /// <summary>
    ///     Pins the public surface of the CRUD pipeline's contract namespaces, and of the workbook and
    ///     assignment contracts other features build against, to stored goldens, and keeps them free of
    ///     the data-access and web frameworks a module must never reference.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A platform minor may add members to these namespaces but never rename or remove one, so
    ///         every change to the surface must show up as a reviewed diff of a golden. To record an
    ///         intended change, run the suite once with <c>TELLMA_UPDATE_GOLDENS=1</c>: the run rewrites
    ///         the goldens and fails on purpose, so a variable left set in an environment can never be
    ///         read as a pass. Review the diff, then run again without it.
    ///     </para>
    /// </remarks>
    public class PublicApiGoldenTests
    {
        /// <summary>The variable that turns a run into a rewrite.</summary>
        private const string UpdateVariable = "TELLMA_UPDATE_GOLDENS";

        /// <summary>The namespace prefixes of the frameworks the contracts must not name.</summary>
        private static readonly string[] ForbiddenNamespaces =
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Data.SqlClient",
            "System.Data.SqlClient",
            "Microsoft.AspNetCore",
        ];

        /// <summary>The pinned namespaces.</summary>
        public static TheoryData<string> Namespaces { get; } =
        [
            "Tellma.Core.Abstractions.Crud",
            "Tellma.Core.Abstractions.Validation",
            "Tellma.Core.Abstractions.Errors",
            "Tellma.Core.Abstractions.Excel",
            "Tellma.Core.Abstractions.Assignments",
        ];

        /// <summary>The contract assembly.</summary>
        private static Assembly Contracts => typeof(StackDescriptor).Assembly;

        /// <summary>Whether this run rewrites the goldens instead of checking them.</summary>
        private static bool Updating => Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true";

        [Theory]
        [MemberData(nameof(Namespaces))]
        public void The_public_surface_matches_its_golden(string ns)
        {
            string rendered = PublicApiRenderer.Render(Contracts, ns);
            string path = Path.Combine(GoldenDirectory(), ns + ".txt");

            if (Updating)
            {
                _ = Directory.CreateDirectory(GoldenDirectory());
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                Assert.Fail("The public API golden was rewritten; review the diff and run again without " + UpdateVariable + " set.");
            }

            Assert.True(File.Exists(path), "No public API golden for " + ns + ". Set " + UpdateVariable + " to record one.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), rendered);
        }

        [Theory]
        [MemberData(nameof(Namespaces))]
        public void The_surface_names_no_data_access_or_web_framework_type(string ns)
        {
            IReadOnlySet<Type> referenced = PublicApiRenderer.ReferencedTypes(Contracts, ns);

            Assert.NotEmpty(referenced);
            Assert.DoesNotContain(referenced, static type => IsForbidden(type.Namespace));
        }

        [Fact]
        public void The_contract_assembly_reaches_no_data_access_or_web_framework_and_no_library_but_queryex()
        {
            // Stronger than the surface check: nothing a module pulls in by referencing the contracts,
            // directly or through a library they reference, may be one of those frameworks.
            string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            HashSet<string> visited = new(StringComparer.Ordinal);
            Queue<Assembly> pending = new([Contracts]);
            List<string> libraries = [];

            // Breadth-first over the reference graph; the shared framework's own assemblies are
            // checked by name but not walked, since they are not the contracts' dependencies to pin.
            while (pending.TryDequeue(out Assembly? assembly))
            {
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    if (!visited.Add(reference.Name!))
                    {
                        continue;
                    }

                    Assert.False(IsForbidden(reference.Name), assembly.GetName().Name + " references " + reference.Name + ".");
                    Assembly loaded = AssemblyLoadContext.Default.LoadFromAssemblyName(reference);
                    if (!string.Equals(Path.GetDirectoryName(loaded.Location), runtime, StringComparison.OrdinalIgnoreCase))
                    {
                        libraries.Add(reference.Name!);
                        pending.Enqueue(loaded);
                    }
                }
            }

            Assert.Equal(["Tellma.Core.Queryex"], libraries);
        }

        [Fact]
        public void The_renderer_renders_what_a_caller_can_name_and_nothing_else()
        {
            string crud = PublicApiRenderer.Render(Contracts, "Tellma.Core.Abstractions.Crud");

            // A protected hook is part of the surface; the explicit interface implementations and
            // the internal pipeline property are not.
            Assert.Contains("    protected virtual Task ValidateAsync(SaveContext<TEntity> context)", crud, StringComparison.Ordinal);
            Assert.DoesNotContain("Pipeline {", crud, StringComparison.Ordinal);
            Assert.DoesNotContain("IEntityBehavior<TEntity, TKey>.ValidateAsync", crud, StringComparison.Ordinal);
            Assert.DoesNotContain("IEnlistmentHost", crud, StringComparison.Ordinal);
            Assert.Contains("public static class CrudTelemetryNames", crud, StringComparison.Ordinal);
            Assert.Contains("    public const string MeterName = \"Tellma.Core\"", crud, StringComparison.Ordinal);

            // Types render as declared: tuple element names, and a type parameter's own annotation.
            Assert.Contains("IReadOnlyList<(string Property, SearchKind Kind)> SearchableProperties", crud, StringComparison.Ordinal);
        }

        [Fact]
        public void The_renderer_renders_type_parameters_as_declared()
        {
            string validation = PublicApiRenderer.Render(Contracts, "Tellma.Core.Abstractions.Validation");

            // An unannotated use of an unconstrained parameter is not nullable; an annotated one is;
            // a notnull constraint lives only in the parameter's own annotation and still shows.
            Assert.Contains("    public T Value { get; }", validation, StringComparison.Ordinal);
            Assert.Contains("    public TEntity? Before(int index)", validation, StringComparison.Ordinal);
            Assert.Contains("ByIds<T, TK>(IReadOnlyList<TK> ids, string? select = null) where TK : notnull", validation, StringComparison.Ordinal);
            Assert.DoesNotContain("TKey?", validation, StringComparison.Ordinal);
        }

        /// <summary>Whether a namespace or assembly name belongs to a forbidden framework.</summary>
        /// <param name="name">The name; null for none.</param>
        /// <returns>True when forbidden.</returns>
        private static bool IsForbidden(string? name)
        {
            return name is not null && ForbiddenNamespaces.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal));
        }

        /// <summary>Where the goldens live: beside this file in the source tree.</summary>
        /// <param name="here">This file's path, supplied by the compiler.</param>
        /// <returns>The directory.</returns>
        private static string GoldenDirectory([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here)!, "Goldens");
        }
    }
}
