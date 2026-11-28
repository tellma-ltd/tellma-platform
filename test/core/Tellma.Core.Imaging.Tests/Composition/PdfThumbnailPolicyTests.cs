// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Composition
{
    /// <summary>
    ///     Which hosts render PDF thumbnails: a Linux host with glibc and Landlock, whatever its
    ///     environment, and without Landlock only in Development; a Linux host with another C
    ///     library, never; a host on Windows or macOS, only in Development. A host that renders none
    ///     can say why.
    /// </summary>
    public sealed class PdfThumbnailPolicyTests
    {
        [Theory]
        [InlineData(true, true, true, false, "Enabled")]
        [InlineData(true, true, true, true, "Enabled")]
        [InlineData(true, true, false, false, "DisabledWithoutLandlock")]
        [InlineData(true, true, false, true, "Enabled")]
        [InlineData(true, false, true, false, "DisabledOnMusl")]
        [InlineData(true, false, true, true, "DisabledOnMusl")]
        [InlineData(true, false, false, true, "DisabledOnMusl")]
        [InlineData(false, false, false, true, "Enabled")]
        [InlineData(false, false, false, false, "DisabledOutsideDevelopment")]
        public void A_host_renders_pdf_thumbnails_on_linux_with_glibc_and_landlock_and_elsewhere_only_in_development(
            bool linux, bool glibc, bool landlock, bool development, string support)
        {
            Assert.Equal(Enum.Parse<PdfThumbnailSupport>(support), PdfThumbnailPolicy.For(linux, glibc, landlock, development));
        }

        [Fact]
        public void This_host_renders_pdf_thumbnails_in_development()
        {
            // The suites run on Windows and on Linux with glibc, with Landlock or without.
            Assert.Equal(PdfThumbnailSupport.Enabled, PdfThumbnailPolicy.For(TestHostEnvironment.Development));
        }

        [Fact]
        public void Outside_development_this_host_renders_pdf_thumbnails_only_on_linux_with_landlock()
        {
            PdfThumbnailSupport expected = !OperatingSystem.IsLinux()
                ? PdfThumbnailSupport.DisabledOutsideDevelopment
                : PdfWorkerLauncher.CanConfine ? PdfThumbnailSupport.Enabled : PdfThumbnailSupport.DisabledWithoutLandlock;

            // A host without an environment is not in Development.
            Assert.Equal(expected, PdfThumbnailPolicy.For(TestHostEnvironment.Production));
            Assert.Equal(expected, PdfThumbnailPolicy.For(new TestHostEnvironment("Staging")));
            Assert.Equal(expected, PdfThumbnailPolicy.For(null));
        }

        [Theory]
        [InlineData("DisabledOutsideDevelopment", "Development")]
        [InlineData("DisabledOnMusl", "glibc")]
        [InlineData("DisabledWithoutLandlock", "Landlock")]
        public void A_host_that_renders_none_says_why_and_what_would_change_it(string support, string remedy)
        {
            string reason = PdfThumbnailPolicy.Reason(Enum.Parse<PdfThumbnailSupport>(support));

            Assert.Contains(remedy, reason, StringComparison.Ordinal);
            Assert.EndsWith(".", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void A_host_that_renders_pdf_thumbnails_has_no_reason_not_to()
        {
            Assert.Throws<ArgumentOutOfRangeException>(static () => PdfThumbnailPolicy.Reason(PdfThumbnailSupport.Enabled));
            Assert.Throws<ArgumentOutOfRangeException>(static () => PdfThumbnailPolicy.Reason((PdfThumbnailSupport)99));
        }
    }
}
