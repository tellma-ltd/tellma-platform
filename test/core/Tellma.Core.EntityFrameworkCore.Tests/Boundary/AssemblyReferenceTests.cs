// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyModel;
using System.Reflection;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.Tests.Boundary
{
    /// <summary>
    ///     The hard runtime/design boundary (spec 0001 Rule 3) and the internal-API quarantine
    ///     (Rule 1), enforced mechanically: assembly-reference checks, a transitive
    ///     dependency-closure check (the publish-output guarantee — a host referencing the runtime
    ///     library can never end up with Design-tree assemblies in its output), and a ground-truth
    ///     scan of this very test app's output directory. (When a real web host exists, its CI
    ///     pipeline should additionally assert the literal publish output.)
    /// </summary>
    public class AssemblyReferenceTests
    {
        private static Assembly RuntimeAssembly => typeof(TableTypesOptionsExtension).Assembly;

        /// <summary>The one namespace allowed to touch EF internal APIs.</summary>
        private const string QuarantineNamespace = "Tellma.Core.EntityFrameworkCore.TableTypes.Internal";

        /// <summary>
        ///     The EF Design dependency tree that must never reach a runtime host (the deny list of
        ///     spec 0001 Rule 3): the Design package itself, Roslyn, templating, Humanizer, and
        ///     Tellma's own design-time companion.
        /// </summary>
        private static readonly string[] DenyListPrefixes =
        [
            "Microsoft.EntityFrameworkCore.Design",
            "Microsoft.CodeAnalysis",
            "Mono.TextTemplating",
            "Humanizer",
            "Tellma.Core.EntityFrameworkCore.Design",
        ];

        private static bool IsDenied(string name)
        {
            return DenyListPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Runtime_assembly_references_no_design_assemblies()
        {
            string[] referenced = [.. RuntimeAssembly.GetReferencedAssemblies().Select(a => a.Name!)];

            Assert.DoesNotContain(referenced, IsDenied);
        }

        [Fact]
        public void Runtime_librarys_transitive_dependency_closure_contains_no_design_packages()
        {
            // The publish-output guarantee, asserted at its source: a framework-dependent publish
            // ships exactly the app's dependency closure, so a host referencing only the runtime
            // library can never receive Design-tree assemblies. Walks this test app's deps.json
            // starting from the runtime library node (the test project deliberately references
            // nothing Tellma but the runtime library and Abstractions).
            DependencyContext context = DependencyContext.Default!;
            var libraries = context.RuntimeLibraries.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);

            HashSet<string> closure = new(StringComparer.OrdinalIgnoreCase);
            Queue<string> pending = new(["Tellma.Core.EntityFrameworkCore"]);
            while (pending.Count > 0)
            {
                string name = pending.Dequeue();
                if (!closure.Add(name) || !libraries.TryGetValue(name, out RuntimeLibrary? library))
                {
                    continue;
                }

                foreach (Dependency dependency in library.Dependencies)
                {
                    pending.Enqueue(dependency.Name);
                }
            }

            // Sanity: the walk found the real graph (otherwise it asserted nothing).
            Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", closure);

            List<string> violations = [.. closure.Where(IsDenied)];
            Assert.True(
                violations.Count == 0,
                "The runtime library's dependency closure must not contain Design-tree packages, but found: "
                    + string.Join(", ", violations) + ". A runtime host referencing the library would ship them.");
        }

        [Fact]
        public void Test_apps_output_directory_contains_no_design_assemblies()
        {
            // Ground truth on disk: this test app's output directory is what any host referencing
            // the runtime library gets (plus test-host bits, which are themselves Design-free).
            List<string> violations = [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                .Select(file => Path.GetFileNameWithoutExtension(file)!)
                .Where(IsDenied)];

            Assert.True(
                File.Exists(Path.Combine(AppContext.BaseDirectory, "Tellma.Core.EntityFrameworkCore.dll")),
                "Sanity check failed: the runtime library is not in the test output.");
            Assert.True(
                violations.Count == 0,
                "Design-tree assemblies found in the output directory: " + string.Join(", ", violations));
        }

        [Fact]
        public void Runtime_assembly_references_no_tellma_application_assemblies()
        {
            // Rule 3: self-contained — EF Core packages only, never Tellma application projects.
            string[] referenced = [.. RuntimeAssembly.GetReferencedAssemblies().Select(a => a.Name!)];

            Assert.DoesNotContain(referenced, name =>
                name.StartsWith("Tellma", StringComparison.Ordinal));
        }

        [Fact]
        public void Internal_ef_api_usage_is_confined_to_the_quarantine_namespace()
        {
            // Signature-level scan: outside the quarantine, no type may expose EF ".Internal"
            // types in its base type, implemented interfaces, or member signatures. (Signatures
            // are what cause ReflectionTypeLoadException when an assembly is absent — the failure
            // mode the quarantine exists to contain.)
            foreach (Type type in RuntimeAssembly.GetTypes())
            {
                if ((type.Namespace ?? string.Empty).StartsWith(QuarantineNamespace, StringComparison.Ordinal))
                {
                    continue;
                }

                List<Type> signatureTypes = [];
                if (type.BaseType is not null)
                {
                    signatureTypes.Add(type.BaseType);
                }

                signatureTypes.AddRange(type.GetInterfaces());

                const BindingFlags Flags =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (MethodInfo method in type.GetMethods(Flags))
                {
                    signatureTypes.Add(method.ReturnType);
                    signatureTypes.AddRange(method.GetParameters().Select(p => p.ParameterType));
                }

                foreach (ConstructorInfo constructor in type.GetConstructors(Flags))
                {
                    signatureTypes.AddRange(constructor.GetParameters().Select(p => p.ParameterType));
                }

                foreach (FieldInfo field in type.GetFields(Flags))
                {
                    signatureTypes.Add(field.FieldType);
                }

                foreach (Type signatureType in signatureTypes)
                {
                    string ns = signatureType.Namespace ?? string.Empty;
                    Assert.False(
                        ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                            && ns.Contains(".Internal", StringComparison.Ordinal),
                        $"Type '{type.FullName}' exposes EF internal type '{signatureType.FullName}' outside the quarantine namespace.");
                }
            }
        }
    }
}
