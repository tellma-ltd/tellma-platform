// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     How a worker is started: <c>dotnet exec</c> on the host's own runtime files, under an
    ///     environment of the worker's runtime settings alone, with a command line that splits
    ///     back into exactly its arguments; and how the worker's sandbox reads the descriptors it
    ///     must close.
    /// </summary>
    public sealed class PdfWorkerLauncherTests
    {
        [Fact]
        public void A_worker_is_started_with_dotnet_exec_on_the_hosts_runtime_files()
        {
            var launcher = new PdfWorkerLauncher(arguments: ["extra"]);
            PdfWorkerFiles files = launcher.Files;

            Assert.Equal(["exec", "--runtimeconfig", files.RuntimeConfiguration, "--depsfile", files.Dependencies, files.Worker, "extra"], launcher.Arguments());
        }

        [Fact]
        public void The_environment_holds_the_workers_runtime_settings_and_nothing_of_the_hosts()
        {
            Dictionary<string, string> environment = PdfWorkerLauncher.Environment();

            Assert.Equal("0", environment["DOTNET_gcServer"]);
            Assert.Equal("0", environment["DOTNET_EnableDiagnostics"]);
            Assert.Equal("0", environment["DOTNET_DbgEnableMiniDump"]);
            Assert.Equal("1", environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"]);
            Assert.All(environment.Keys, static name => Assert.True(name.StartsWith("DOTNET_", StringComparison.Ordinal) || name == "SystemRoot", name));
            Assert.Equal(OperatingSystem.IsWindows(), environment.ContainsKey("SystemRoot"));
        }

        [Theory]
        [InlineData(@"C:\Program Files\dotnet\dotnet.exe", @"""C:\Program Files\dotnet\dotnet.exe""")]
        [InlineData(@"plain", @"""plain""")]
        [InlineData(@"a""b", @"""a\""b""")]
        [InlineData(@"trailing\", @"""trailing\\""")]
        [InlineData(@"x\\""y", @"""x\\\\\""y""")]
        [InlineData(@"back\slash", @"""back\slash""")]
        [InlineData("", @"""""")]
        public void Each_argument_is_quoted_so_the_c_runtime_splits_it_back_unchanged(string argument, string quoted)
        {
            Assert.Equal($"\"app\" {quoted}", Win32Text.CommandLine("app", [argument]));
        }

        [Fact]
        public void The_environment_block_is_sorted_and_double_terminated()
        {
            string block = Win32Text.EnvironmentBlock(new Dictionary<string, string> { ["b"] = "2", ["A"] = "1" });

            Assert.Equal("A=1\0b=2\0\0", block);
        }

        [Fact]
        public void The_sandbox_takes_its_descriptor_arguments_and_leaves_the_rest()
        {
            // Only the parsing: entering the sandbox itself would filter this very process.
            List<string> arguments = ["mode", WorkerSandbox.CloseDescriptorArgument, "7", "pipe:[1]", "other", WorkerSandbox.CloseDescriptorArgument, "9", "/tmp/a b"];

            List<(int Descriptor, string Target)> descriptors = WorkerSandbox.TakeInheritedDescriptors(arguments);

            Assert.Equal([(7, "pipe:[1]"), (9, "/tmp/a b")], descriptors);
            Assert.Equal(["mode", "other"], arguments);
        }

        [Theory]
        [InlineData("2", "x")]
        [InlineData("-1", "x")]
        [InlineData("seven", "x")]
        public void A_malformed_descriptor_argument_is_refused(string descriptor, string target)
        {
            Assert.Throws<ArgumentException>(() => WorkerSandbox.TakeInheritedDescriptors([WorkerSandbox.CloseDescriptorArgument, descriptor, target]));
            Assert.Throws<ArgumentException>(() => WorkerSandbox.TakeInheritedDescriptors([WorkerSandbox.CloseDescriptorArgument, "7"]));
        }
    }
}
