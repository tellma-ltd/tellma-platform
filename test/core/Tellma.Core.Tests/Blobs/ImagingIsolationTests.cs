// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The runtime never carries the imaging package or its native libraries: it binds to the
    ///     image-processor contract alone, so a host that does not add a processor loads no Skia or
    ///     PDFium.
    /// </summary>
    public class ImagingIsolationTests
    {
        // The imaging package and the libraries only it may bring.
        private static readonly string[] ImagingAssemblies = ["Tellma.Core.Imaging", "SkiaSharp", "PDFtoImage"];

        [Fact]
        public void The_runtime_assembly_references_no_imaging_assembly()
        {
            string[] referenced = [.. typeof(BlobsComposition).Assembly.GetReferencedAssemblies().Select(static name => name.Name!)];

            Assert.Empty(referenced.Intersect(ImagingAssemblies, StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void A_host_of_the_runtime_alone_has_no_imaging_assembly_or_native_library_in_its_output()
        {
            // This suite references the runtime and no imaging package, so its output directory is
            // what such a host ships.
            string output = AppContext.BaseDirectory;
            string[] files = [.. Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Select(Path.GetFileName)!];

            Assert.DoesNotContain(files, static file => ImagingAssemblies.Any(name => file.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(files, static file => file.Contains("SkiaSharp", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(files, static file => file.Contains("pdfium", StringComparison.OrdinalIgnoreCase));
        }
    }
}
