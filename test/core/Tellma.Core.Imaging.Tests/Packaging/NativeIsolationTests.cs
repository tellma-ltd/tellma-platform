// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;

namespace Tellma.Core.Imaging.Tests.Packaging
{
    /// <summary>
    ///     The package's dependency edges and native payload: it references no Tellma assembly but
    ///     the abstractions and its PDF worker, never loads PDFium itself, and its output carries both
    ///     native libraries for every server platform the platform deploys to.
    /// </summary>
    public sealed class NativeIsolationTests
    {
        public static TheoryData<string, string> NativeLibraries => new()
        {
            { "win-x64", "libSkiaSharp.dll" },
            { "win-x64", "pdfium.dll" },
            { "linux-x64", "libSkiaSharp.so" },
            { "linux-x64", "libpdfium.so" },
            { "linux-arm64", "libSkiaSharp.so" },
            { "linux-arm64", "libpdfium.so" },
        };

        [Theory]
        [MemberData(nameof(NativeLibraries))]
        public void The_output_carries_each_native_library_for_each_server_platform(string runtime, string library)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "runtimes", runtime, "native", library);

            Assert.True(File.Exists(path), $"{path} is missing.");
        }

        [Fact]
        public void No_native_symbol_file_reaches_the_output_or_the_dependency_manifest()
        {
            string runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
            string manifest = Path.Combine(AppContext.BaseDirectory, "Tellma.Core.Imaging.Tests.deps.json");

            Assert.Empty(Directory.EnumerateFiles(runtimes, "*.pdb", SearchOption.AllDirectories));
            Assert.DoesNotContain(".pdb", File.ReadAllText(manifest), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_package_references_no_tellma_assembly_but_the_abstractions_and_its_worker()
        {
            string[] tellma = [.. typeof(SkiaImageProcessor).Assembly.GetReferencedAssemblies()
                .Select(static name => name.Name!)
                .Where(static name => name.StartsWith("Tellma.", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];

            Assert.Equal(["Tellma.Core.Abstractions", "Tellma.Core.Imaging.PdfWorker"], tellma);
        }

        [Fact]
        public void The_worker_references_no_tellma_assembly_and_only_pdftoimage_and_skia()
        {
            string[] references = [.. typeof(PdfWorker.WorkerProtocol).Assembly.GetReferencedAssemblies()
                .Select(static name => name.Name!)
                .Where(static name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard")
                .Order(StringComparer.Ordinal)];

            Assert.Equal(["PDFtoImage", "SkiaSharp"], references);
        }

        [Fact]
        public void The_package_references_only_skia_beyond_the_framework_and_extensions()
        {
            string[] thirdParty = [.. typeof(SkiaImageProcessor).Assembly.GetReferencedAssemblies()
                .Select(static name => name.Name!)
                .Where(static name => !name.StartsWith("System", StringComparison.Ordinal)
                    && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                    && !name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal)
                    && !name.StartsWith("Tellma.", StringComparison.Ordinal)
                    && name != "netstandard")
                .Order(StringComparer.Ordinal)];

            Assert.Equal(["SkiaSharp"], thirdParty);
        }

        [Fact]
        public void The_loaded_skia_library_is_the_pinned_milestone()
        {
            // SkiaSharp refuses a native library of another milestone; this pins the pair together.
            Assembly skia = typeof(SkiaSharp.SKCodec).Assembly;

            Assert.StartsWith("4.152.", skia.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, StringComparison.Ordinal);
            Assert.NotNull(SkiaSharp.SkiaSharpVersion.Native);
        }
    }
}
