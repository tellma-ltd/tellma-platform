// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Migrator;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>
    ///     The migrator's entry point without a database: its exit codes, the startup gate ahead of
    ///     every command, the job worker forced off, and the emit's composition-only run and files.
    /// </summary>
    public class TellmaMigratorTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Migrate_in_process_identity_mode_without_the_identity_store_migration_is_a_usage_error()
        {
            Run run = await RunAsync(["migrate"], configuration: TestComposition.Sound(("Tellma:Identity:Mode", "InProc")));

            Assert.Equal(MigratorExitCodes.Usage, run.ExitCode);
            Assert.Contains(TellmaMigrator.IdentityMigrationsAssembly, run.Error, StringComparison.Ordinal);
            Assert.Empty(run.Output);
        }

        [Fact]
        public async Task An_unknown_command_is_a_usage_error_that_prints_the_usage()
        {
            Run run = await RunAsync(["upgrade"]);

            Assert.Equal(MigratorExitCodes.Usage, run.ExitCode);
            Assert.Contains("'upgrade' is not a migrator command.", run.Error, StringComparison.Ordinal);
            Assert.Contains("emit-client-assets <dir>", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_composition_problem_exits_with_the_composition_code_before_any_command()
        {
            Run run = await RunAsync(["status"], compose: static tellma => tellma.AddFeature(new PlainFeature()).AddFeature(new PlainFeature()));

            Assert.Equal(MigratorExitCodes.CompositionProblems, run.ExitCode);
            Assert.Contains("The Tellma composition has", run.Error, StringComparison.Ordinal);
            Assert.Empty(run.Output);
        }

        [Fact]
        public async Task The_startup_gate_runs_before_the_command_and_its_problems_stop_it()
        {
            string key = Key();

            Run run = await RunAsync(
                ["status"],
                compose: static tellma => tellma.AddFeature(new TestFeature<Tag.A>("probe", contribute: static c => c.Singleton<IStartupCheck, GateProbe>())),
                configuration: TestComposition.Sound((GateProbe.RunKey, key), (GateProbe.FailKey, "true")));

            // The status command would have printed the model's fingerprint and reached for the catalog.
            Assert.Equal(MigratorExitCodes.CompositionProblems, run.ExitCode);
            Assert.Contains("[test.gate] The gate probe refused the start.", run.Error, StringComparison.Ordinal);
            Assert.Empty(run.Output);
            Assert.Single(GateProbe.Seen(key));
        }

        [Fact]
        public async Task The_command_runs_once_the_gate_passed_with_the_job_worker_forced_off()
        {
            string key = Key();

            // A deployed provision refuses an administrator subject inside the command, before any database.
            Run run = await RunAsync(
                ["provision", "--tenant", "1", "--admin-subject", "someone"],
                compose: static tellma => tellma.AddFeature(new TestFeature<Tag.A>("probe", contribute: static c => c.Singleton<IStartupCheck, GateProbe>())),
                configuration: TestComposition.Sound((GateProbe.RunKey, key), (TellmaMigrator.JobsEnabledKey, "true")));

            Assert.Equal(MigratorExitCodes.Usage, run.ExitCode);
            Assert.Contains("--admin-subject is accepted in Development only", run.Error, StringComparison.Ordinal);
            Assert.Equal(["false"], GateProbe.Seen(key));
        }

        [Fact]
        public async Task Emit_runs_no_infrastructure_check_and_needs_no_database()
        {
            using EmitDirectory directory = new();

            Run run = await RunAsync(
                ["emit-client-assets", directory.Path],
                compose: static tellma => tellma.AddFeature(new TestFeature<Tag.A>("probe", contribute: static c => c.Singleton<IStartupCheck, FailingInfrastructureCheck>())),
                configuration: new Dictionary<string, string?> { ["Tellma:DisplayName"] = "Acme ERP" });

            Assert.True(run.ExitCode == MigratorExitCodes.Converged, run.Error);
            Assert.True(File.Exists(Path.Combine(directory.Path, "tellma-metadata.ts")));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(directory.Path, "assets")).Length);
        }

        [Fact]
        public async Task Emit_exits_with_the_composition_code_on_a_composition_only_checks_problem_and_writes_nothing()
        {
            using EmitDirectory directory = new();

            Run run = await RunAsync(
                ["emit-client-assets", directory.Path],
                compose: static tellma => tellma.AddFeature(new TestFeature<Tag.A>("probe", contribute: static c => c.Singleton<IStartupCheck, FailingCheck>())),
                configuration: new Dictionary<string, string?> { ["Tellma:DisplayName"] = "Acme ERP" });

            Assert.Equal(MigratorExitCodes.CompositionProblems, run.ExitCode);
            Assert.Contains("[test.failing]", run.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(directory.Path) && Directory.EnumerateFileSystemEntries(directory.Path).Any());
        }

        [Fact]
        public async Task Emit_without_a_display_name_reports_the_core_contributors_problem()
        {
            using EmitDirectory directory = new();

            Run run = await RunAsync(["emit-client-assets", directory.Path], configuration: new Dictionary<string, string?>());

            Assert.Equal(MigratorExitCodes.CompositionProblems, run.ExitCode);
            Assert.Contains("Tellma:DisplayName is not set", run.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Emit_writes_the_core_metadata_deterministically_and_rewrites_nothing_unchanged()
        {
            using EmitDirectory directory = new();
            Dictionary<string, string?> configuration = new() { ["Tellma:DisplayName"] = "Acme ERP", ["Tellma:Copyright"] = "© Acme" };

            // --- A stale chunk of an earlier build, and a file the emit does not own ---
            string assets = Path.Combine(directory.Path, "assets");
            _ = Directory.CreateDirectory(assets);
            await File.WriteAllTextAsync(Path.Combine(assets, "core-0000000000AB.json"), "{}", Token);
            await File.WriteAllTextAsync(Path.Combine(assets, "logo.svg"), "<svg/>", Token);

            Run first = await RunAsync(["emit-client-assets", directory.Path], configuration: configuration);
            Dictionary<string, byte[]> written = Snapshot(directory.Path);
            Run second = await RunAsync(["emit-client-assets", directory.Path], configuration: configuration);

            // --- The first run writes and sweeps; the second finds every file current ---
            Assert.True(first.ExitCode == MigratorExitCodes.Converged, first.Error);
            Assert.Contains("removed assets/core-0000000000AB.json", first.Output, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(assets, "logo.svg")));
            Assert.Equal(MigratorExitCodes.Converged, second.ExitCode);
            Assert.All(second.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), static line => Assert.StartsWith("unchanged ", line, StringComparison.Ordinal));
            Assert.Equal(written.Keys.Order(StringComparer.Ordinal), Snapshot(directory.Path).Keys.Order(StringComparer.Ordinal));

            // --- The core chunk's platform sections ---
            using var core = JsonDocument.Parse(written.Single(static file => file.Key.StartsWith("assets/core-", StringComparison.Ordinal)).Value);
            JsonElement distribution = core.RootElement.GetProperty("distribution");
            Assert.Equal(1, core.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal(TestComposition.Slug, distribution.GetProperty("slug").GetString());
            Assert.Equal("Acme ERP", distribution.GetProperty("displayName").GetString());
            Assert.Equal("© Acme", distribution.GetProperty("copyright").GetString());
            Assert.False(string.IsNullOrEmpty(distribution.GetProperty("platformVersion").GetString()));
            Assert.Equal("core", core.RootElement.GetProperty("features")[0].GetProperty("key").GetString());
            Assert.Contains("api", core.RootElement.GetProperty("routing").GetProperty("serverFirstSegments").EnumerateArray().Select(static segment => segment.GetString()));
        }

        /// <summary>A unique key for one test's probe records.</summary>
        /// <returns>The key.</returns>
        private static string Key()
        {
            return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        /// <summary>Every file under a folder and its bytes, by forward-slash relative path.</summary>
        /// <param name="root">The folder.</param>
        /// <returns>The files.</returns>
        private static Dictionary<string, byte[]> Snapshot(string root)
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
        }

        /// <summary>Runs the migrator in process with in-memory configuration, in Production.</summary>
        /// <param name="args">The command line.</param>
        /// <param name="compose">The composition; the core feature alone when null.</param>
        /// <param name="configuration">The configuration; the sound one when null.</param>
        /// <returns>What the run printed and returned.</returns>
        private static async Task<Run> RunAsync(
            string[] args, Action<TellmaBuilder>? compose = null, IReadOnlyDictionary<string, string?>? configuration = null)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            IReadOnlyDictionary<string, string?> values = configuration ?? TestComposition.SoundConfiguration;

            int code = await TellmaMigrator.RunAsync(
                args,
                TestComposition.Slug,
                compose ?? (static _ => { }),
                migrateIdentityStore: null,
                new MigratorRunSettings
                {
                    MigrationsAssembly = typeof(TellmaMigratorTests).Assembly,
                    Out = output,
                    Error = error,
                    EnvironmentName = "Production",
                    ContentRootPath = AppContext.BaseDirectory,
                    ConfigureConfiguration = configurationBuilder => configurationBuilder.AddInMemoryCollection(values),
                    ConfigureServices = static services => services.AddLogging(static logging => logging.ClearProviders()),
                },
                Token);

            return new Run(code, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
        }

        /// <summary>What one run printed and returned.</summary>
        /// <param name="ExitCode">The exit code.</param>
        /// <param name="Output">The report.</param>
        /// <param name="Error">Usage errors and composition problems.</param>
        private sealed record Run(int ExitCode, string Output, string Error);

        /// <summary>A generated folder in the temporary directory, deleted with its contents at the end.</summary>
        private sealed class EmitDirectory : IDisposable
        {
            /// <summary>The folder's full path; it does not exist until written.</summary>
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tellma-emit-" + Key(), "generated");

            /// <inheritdoc />
            public void Dispose()
            {
                string parent = System.IO.Path.GetDirectoryName(Path)!;
                if (Directory.Exists(parent))
                {
                    Directory.Delete(parent, recursive: true);
                }
            }
        }

        /// <summary>
        ///     A composition-only startup check that records the job worker's setting each time it runs,
        ///     and fails when told to.
        /// </summary>
        /// <param name="configuration">The host's configuration.</param>
        private sealed class GateProbe(IConfiguration configuration) : IStartupCheck
        {
            /// <summary>The configuration key of the test's run key.</summary>
            public const string RunKey = "Probe:RunKey";

            /// <summary>The configuration key that makes the check fail.</summary>
            public const string FailKey = "Probe:Fail";

            /// <summary>Each run key's recorded settings.</summary>
            private static readonly ConcurrentDictionary<string, ConcurrentQueue<string?>> Runs = new(StringComparer.Ordinal);

            /// <inheritdoc />
            public string Name => "test.gate";

            /// <inheritdoc />
            public bool RequiresInfrastructure => false;

            /// <summary>The settings recorded under a run key.</summary>
            /// <param name="key">The run key.</param>
            /// <returns>The job worker's setting at each run.</returns>
            public static IReadOnlyList<string?> Seen(string key)
            {
                return Runs.TryGetValue(key, out ConcurrentQueue<string?>? seen) ? [.. seen] : [];
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
            {
                Runs.GetOrAdd(configuration[RunKey] ?? string.Empty, static _ => new()).Enqueue(configuration[TellmaMigrator.JobsEnabledKey]);
                IReadOnlyList<CompositionProblem> problems = configuration[FailKey] == "true"
                    ? [new CompositionProblem(Name, "The gate probe refused the start.", null)]
                    : [];
                return Task.FromResult(problems);
            }
        }
    }
}
