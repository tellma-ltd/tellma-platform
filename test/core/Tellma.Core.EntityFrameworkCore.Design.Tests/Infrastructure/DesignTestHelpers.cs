// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.Loader;

namespace Tellma.Core.EntityFrameworkCore.Design.Tests.Infrastructure
{
    /// <summary>
    ///     Helpers for design-time tests: building the design-time service provider the way EF
    ///     tooling does (referenced services first, EF defaults after with TryAdd semantics), and
    ///     compiling scaffolded C# in memory with Roslyn.
    /// </summary>
    public static class DesignTestHelpers
    {
        /// <summary>
        ///     Builds the design-time service provider for a context, mirroring EF tooling's
        ///     ordering: Tellma's referenced services apply before EF's TryAdd defaults.
        /// </summary>
        public static ServiceProvider BuildDesignServices(DbContext context)
        {
            ServiceCollection services = new();
            new TableTypesDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning disable EF1001 // The provider's design-time services bundle, exactly as EF tooling loads it.
            new Microsoft.EntityFrameworkCore.SqlServer.Design.Internal.SqlServerDesignTimeServices()
                .ConfigureDesignTimeServices(services);
#pragma warning restore EF1001
            services
                .AddEntityFrameworkDesignTimeServices()
                .AddDbContextDesignTimeServices(context);
            return services.BuildServiceProvider();
        }

        /// <summary>
        ///     Compiles C# source in memory against everything currently loaded plus the trusted
        ///     platform assemblies, and loads the result into a collectible
        ///     <see cref="AssemblyLoadContext" />.
        /// </summary>
        public static Assembly Compile(string source, string assemblyName)
        {
            Dictionary<string, MetadataReference> references = [];
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
            {
                foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
                {
                    references[Path.GetFileName(path)] = MetadataReference.CreateFromFile(path);
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references[Path.GetFileName(assembly.Location)] = MetadataReference.CreateFromFile(assembly.Location);
                }
            }

            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                references.Values,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using MemoryStream stream = new();
            Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
            Assert.True(
                result.Success,
                "Scaffolded code failed to compile:\n"
                    + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    + "\n--- source ---\n" + source);

            stream.Position = 0;
            AssemblyLoadContext loadContext = new(assemblyName, isCollectible: true);
            return loadContext.LoadFromStream(stream);
        }
    }
}
