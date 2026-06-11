// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.Tests.Boundary
{
    /// <summary>
    ///     The hard runtime/design boundary (spec Rule 3) and the internal-API quarantine (Rule 1),
    ///     enforced mechanically. The publish-output half of Rule 3 runs in CI against the
    ///     BoundaryHost asset (<c>eng/check-publish-boundary.ps1</c>).
    /// </summary>
    public class AssemblyReferenceTests
    {
        private static Assembly RuntimeAssembly => typeof(TableTypesOptionsExtension).Assembly;

        /// <summary>The one namespace allowed to touch EF internal APIs.</summary>
        private const string QuarantineNamespace = "Tellma.Core.EntityFrameworkCore.TableTypes.Internal";

        [Fact]
        public void Runtime_assembly_references_no_design_assemblies()
        {
            string[] referenced = [.. RuntimeAssembly.GetReferencedAssemblies().Select(a => a.Name!)];

            Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Design", referenced);
            Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
            Assert.DoesNotContain(referenced, name => name.StartsWith("Mono.TextTemplating", StringComparison.Ordinal));
            Assert.DoesNotContain(referenced, name => name.StartsWith("Humanizer", StringComparison.Ordinal));
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
