// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Migrator;

namespace Tellma.Core.IntegrationTests.Migrator
{
    /// <summary>
    ///     The migrator's database creation while the server's <c>model</c> database is locked, as it
    ///     is while another creation copies it: a creation that cannot take the lock is run again
    ///     after a logged wait and converges once the lock is free, one out of attempts gives up
    ///     having created nothing, and a creation that runs past its bound is never run again.
    /// </summary>
    /// <remarks>
    ///     A session whose database is <c>model</c> holds a lock on it that every creation on the
    ///     server needs exclusively, so it stalls every other suite's creations while it is open: the
    ///     suite runs alone, after the parallel ones.
    /// </remarks>
    /// <param name="sql">The suite's server.</param>
    [Trait("Category", "Integration")]
    [Collection(SerialSuites.Name)]
    public class ModelLockTests(SqlServerFixture sql)
    {
        /// <summary>The event a creation that will be run again logs.</summary>
        private const string Retrying = "MigratorDatabaseCreationRetrying";

        /// <summary>The event a creation that ran out of attempts logs.</summary>
        private const string GaveUp = "MigratorDatabaseCreationGaveUp";

        /// <summary>
        ///     Two runs create their catalogs while <c>model</c> is held. The one allowed a single
        ///     attempt gives up and creates nothing; the one allowed the default attempts logs its
        ///     wait, and converges on the run after the lock is released, its catalog prepared as any
        ///     fresh one.
        /// </summary>
        /// <returns>A task that completes when the test has run.</returns>
        [Fact]
        public async Task A_creation_that_finds_the_model_database_locked_is_run_again_and_converges_once_it_is_free()
        {
            EventRecorder patientLog = new(Retrying);
            MigratorHarness patient = new(sql, "ml_patient") { Services = patientLog.Register };
            EventRecorder loneLog = new(Retrying);
            MigratorHarness lone = new(sql, "ml_lone") { Services = loneLog.Register };
            lone.Values[$"{MigratorOptions.SectionPath}:{nameof(MigratorOptions.CreateDatabaseAttempts)}"] = "1";

            // --- Both runs create their catalogs while model is held ---
            // The lone run ends after its one attempt; the patient run is held until it has logged
            // a wait, so its next attempt finds model free. A run that ends before logging one means
            // model was never locked, which the assertions below report.
            MigratorResult loneResult;
            bool patientWaited;
            Task<MigratorResult> patientRun;
            SqlConnection holder = await HoldModelAsync();
            await using (holder.ConfigureAwait(false))
            {
                patientRun = patient.RunAsync("migrate", "--catalog-only");
                Task<MigratorResult> loneRun = lone.RunAsync("migrate", "--catalog-only");
                loneResult = await loneRun;
                patientWaited = await Task.WhenAny(patientLog.Awaited, patientRun) == patientLog.Awaited;
            }

            MigratorResult patientResult = await patientRun;

            // --- The lone run gave up after its one attempt and created nothing ---
            Assert.True(loneResult.ExitCode == MigratorExitCodes.PartialFailure, loneResult.ToString());
            string failure = Assert.Single(loneResult.Lines, static line => line.StartsWith("catalog: failed (", StringComparison.Ordinal));
            Assert.Contains("Could not obtain exclusive lock on database 'model'", failure, StringComparison.Ordinal);
            Assert.Empty(loneLog.Named(Retrying));
            IReadOnlyDictionary<string, object?> gaveUp = Assert.Single(loneLog.Named(GaveUp));
            Assert.Equal(lone.CatalogName, gaveUp["Database"]);
            Assert.Equal(1, gaveUp["Attempts"]);
            Assert.Null(await SqlServerFixture.ScalarAsync<int?>(sql.MasterConnectionString, $"SELECT DB_ID(N'{lone.CatalogName}');"));

            // --- The patient run waited, then converged with its catalog prepared ---
            Assert.True(patientWaited, patientResult.ToString());
            Assert.True(patientResult.ExitCode == MigratorExitCodes.Converged, patientResult.ToString());
            IReadOnlyDictionary<string, object?> retried = patientLog.Named(Retrying)[0];
            Assert.Equal(patient.CatalogName, retried["Database"]);
            Assert.Equal(1, retried["Attempt"]);
            Assert.Equal(5, retried["MaxAttempts"]);
            TimeSpan delay = Assert.IsType<TimeSpan>(retried["RetryDelay"]);
            Assert.InRange(delay, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5));
            Assert.Empty(patientLog.Named(GaveUp));
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(sql.MasterConnectionString, $"SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(N'{patient.CatalogName}', 'Collation'));"));
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(sql.MasterConnectionString, $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{patient.CatalogName}';"));
        }

        /// <summary>
        ///     A creation still waiting for <c>model</c> when its bound runs out fails with the
        ///     timeout and is not run again: the server may yet finish a statement its client stopped
        ///     waiting for.
        /// </summary>
        /// <returns>A task that completes when the test has run.</returns>
        [Fact]
        public async Task A_creation_that_runs_past_its_bound_is_not_run_again()
        {
            EventRecorder log = new(Retrying);
            MigratorHarness migrator = new(sql, "ml_timeout") { Services = log.Register };
            migrator.Values[$"{MigratorOptions.SectionPath}:{nameof(MigratorOptions.DatabaseCommandTimeout)}"] = "00:00:01";

            MigratorResult result;
            SqlConnection holder = await HoldModelAsync();
            await using (holder.ConfigureAwait(false))
            {
                result = await migrator.RunAsync("migrate", "--catalog-only");
            }

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            string failure = Assert.Single(result.Lines, static line => line.StartsWith("catalog: failed (", StringComparison.Ordinal));
            Assert.Contains("Execution Timeout Expired", failure, StringComparison.Ordinal);
            Assert.Empty(log.Named(Retrying));
            Assert.Empty(log.Named(GaveUp));
        }

        /// <summary>
        ///     Opens a session whose database is <c>model</c>, which holds the lock every creation on
        ///     the server needs exclusively until the session is closed. The connection is not pooled,
        ///     so closing it ends the session and frees the lock at once.
        /// </summary>
        /// <returns>The open connection; disposing it frees <c>model</c>.</returns>
        private async Task<SqlConnection> HoldModelAsync()
        {
            SqlConnectionStringBuilder builder = new(sql.MasterConnectionString)
            {
                InitialCatalog = "model",
                Pooling = false,
            };
            SqlConnection holder = new(builder.ConnectionString);
            try
            {
                await holder.OpenAsync(TestContext.Current.CancellationToken);
                return holder;
            }
            catch
            {
                await holder.DisposeAsync();
                throw;
            }
        }

        /// <summary>
        ///     Records the migrator's events by name with their structured values, and completes a
        ///     task when the event awaited first arrives.
        /// </summary>
        /// <param name="awaited">The name of the event <see cref="Awaited" /> waits for.</param>
        private sealed class EventRecorder(string awaited) : ILoggerProvider
        {
            /// <summary>The events received so far, with their names.</summary>
            private readonly ConcurrentQueue<(string Name, IReadOnlyDictionary<string, object?> Values)> _events = new();

            /// <summary>Completed when the awaited event first arrives.</summary>
            private readonly TaskCompletionSource _awaited = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Completes when the awaited event first arrives.</summary>
            public Task Awaited => _awaited.Task;

            /// <summary>Adds the recorder to a run's logging, after the harness cleared every other provider.</summary>
            /// <param name="services">The run's services.</param>
            public void Register(IServiceCollection services)
            {
                _ = services.AddLogging(logging => logging.AddProvider(this));
            }

            /// <summary>The structured values of every event received under a name, in order.</summary>
            /// <param name="name">The event's name.</param>
            /// <returns>The events' values.</returns>
            public IReadOnlyList<IReadOnlyDictionary<string, object?>> Named(string name)
            {
                return [.. _events.Where(entry => entry.Name == name).Select(static entry => entry.Values)];
            }

            /// <inheritdoc />
            public ILogger CreateLogger(string categoryName)
            {
                // Only the migrator's own category is recorded; every other logger discards its events.
                return new Logger(categoryName == MigratorLog.Category ? this : null);
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }

            /// <summary>Records one event and completes the awaited task when it is the awaited event.</summary>
            /// <param name="name">The event's name.</param>
            /// <param name="values">Its structured values.</param>
            private void Record(string name, IReadOnlyDictionary<string, object?> values)
            {
                _events.Enqueue((name, values));
                if (name == awaited)
                {
                    _ = _awaited.TrySetResult();
                }
            }

            /// <summary>A logger that hands every named event's structured values to its recorder, if it has one.</summary>
            /// <param name="recorder">The recorder; null for a category that is not recorded.</param>
            private sealed class Logger(EventRecorder? recorder) : ILogger
            {
                /// <inheritdoc />
                public IDisposable? BeginScope<TState>(TState state)
                    where TState : notnull
                {
                    return null;
                }

                /// <inheritdoc />
                public bool IsEnabled(LogLevel logLevel)
                {
                    return recorder is not null;
                }

                /// <inheritdoc />
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    // A named event's state is its message's values, each under its placeholder's name.
                    if (recorder is not null && eventId.Name is { } name && state is IEnumerable<KeyValuePair<string, object?>> values)
                    {
                        recorder.Record(name, values.ToDictionary(StringComparer.Ordinal));
                    }
                }
            }
        }
    }
}
