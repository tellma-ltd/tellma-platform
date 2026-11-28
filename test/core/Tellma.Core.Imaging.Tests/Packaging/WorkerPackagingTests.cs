// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Packaging
{
    /// <summary>
    ///     How the PDF worker ships: its assembly lies beside the host's and the host's dependency
    ///     manifest lists it, it carries no app host, runtime configuration or manifest of its own
    ///     (which would collide with the host's at publish), and the launcher finds everything it
    ///     starts workers from.
    /// </summary>
    public sealed class WorkerPackagingTests
    {
        private const string Worker = "Tellma.Core.Imaging.PdfWorker";

        [Fact]
        public void The_worker_assembly_lies_beside_the_hosts_and_its_manifest_lists_it()
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, $"{Worker}.dll")));
            Assert.Contains($"{Worker}.dll", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Tellma.Core.Imaging.Tests.deps.json")), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(".runtimeconfig.json")]
        [InlineData(".deps.json")]
        [InlineData(".exe")]
        [InlineData("")]
        public void The_worker_carries_no_runtime_files_of_its_own(string suffix)
        {
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, Worker + suffix)), $"{Worker}{suffix} is in the output.");
        }

        [Fact]
        public void The_launcher_starts_workers_from_the_hosts_own_files()
        {
            PdfWorkerFiles files = new PdfWorkerLauncher().Files;

            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Tellma.Core.Imaging.Tests.deps.json"), files.Dependencies, ignoreCase: OperatingSystem.IsWindows());
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Tellma.Core.Imaging.Tests.runtimeconfig.json"), files.RuntimeConfiguration, ignoreCase: OperatingSystem.IsWindows());
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, $"{Worker}.dll"), files.Worker, ignoreCase: OperatingSystem.IsWindows());
            Assert.True(File.Exists(files.Muxer), $"{files.Muxer} is missing.");
            Assert.Equal(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", Path.GetFileName(files.Muxer));
        }

        [Fact]
        public void A_missing_worker_assembly_is_a_start_failure_that_names_it()
        {
            var launcher = new PdfWorkerLauncher(Path.Combine(AppContext.BaseDirectory, "missing.dll"));

            PdfWorkerException exception = Assert.Throws<PdfWorkerException>(() => launcher.Files);
            Assert.Equal(PdfWorkerFailure.Start, exception.Failure);
            Assert.Contains("PDF worker assembly", exception.Message, StringComparison.Ordinal);
        }
    }
}
