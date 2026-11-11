// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.TableTypes;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     The retry and commit-probe matrix under injected faults: a session killed while its batch
    ///     waits before its commit, a connection lost after the server committed, a probe that waits
    ///     for a round trip still running, a deadlock, and the re-run that collides with the rows a
    ///     not-committed verdict missed.
    /// </summary>
    /// <remarks>
    ///     A batch reaches a known point through a "ready" statement: it returns the session id and
    ///     flushes it with a NOWAIT message, so its reader runs while the server goes on. A "gate"
    ///     statement after it then blocks on a row lock a side transaction holds, which keeps the
    ///     server inside the batch, before its commit, until the test opens the gate.
    /// </remarks>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class DataBatchFaultTests(DataBatchDatabase fixture) : IClassFixture<DataBatchDatabase>, IAsyncLifetime
    {
        /// <summary>The id the probed batches insert.</summary>
        private const int ProbedId = 1001;

        /// <summary>The tag row the gate's side transaction locks.</summary>
        private const string GateName = "fx-gate";

        /// <summary>The ready statement: the session id, flushed at once.</summary>
        private const string ReadyText = "SELECT @@SPID AS [Spid];\nRAISERROR(N'', 0, 1) WITH NOWAIT;";

        /// <summary>An insert of the probed id outside any batch.</summary>
        private const string InsertProbedJob = "INSERT INTO [core].[Jobs] ([Id]) VALUES (1001);";

        /// <summary>The stand-in jobs table, whose rows the probe counts.</summary>
        private static readonly TableName Jobs = new("core", "Jobs");

        /// <summary>The stand-in version-tag table, whose rows the gate and the deadlock lock.</summary>
        private static readonly TableName VersionTags = new("core", "VersionTags");

        /// <summary>The gate: a read that waits for the side transaction's lock on the gate row.</summary>
        private static readonly FormattableString GateText =
            $"DECLARE @gate int = (SELECT COUNT(*) FROM [core].[VersionTags] WITH (READCOMMITTEDLOCK) WHERE [Name] = N'fx-gate');";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task A_session_killed_before_its_commit_is_probed_not_committed_and_re_run()
        {
            await SeedTagsAsync(GateName);
            await using Gate gate = await Gate.CloseAsync(fixture.Tenant.ConnectionString);
            using Instruments instruments = new(fixture);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();

            DataBatch batch = Batch(scope, BatchPurpose.Persist);
            _ = batch.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({ProbedId});", new SqlOptions(writes: Set(Jobs)));
            batch.DeclareInsertedRoots(Jobs, [ProbedId]);
            Ready(batch, async (attempt, spid) =>
            {
                // The first attempt dies holding its uncommitted insert; the second runs through.
                if (attempt == 1)
                {
                    await KillAsync(spid);
                }
                else
                {
                    await gate.OpenAsync();
                }
            });
            _ = batch.Sql(GateText, null);

            BatchOutcome outcome = await batch.ExecuteAsync(Token);

            // --- One row, from the re-run: the attempt, the probe and the re-run are three round trips ---
            Assert.Equal(1, await JobCountAsync());
            Assert.Equal((1, 3), (access.Retries, outcome.RoundTrips));
            Assert.Equal([DataMetrics.ProbeNotCommitted], instruments.Probes());
            Assert.Equal([DataMetrics.AmbiguousClass], instruments.RetryClasses());
        }

        [Fact]
        public async Task A_connection_lost_after_the_server_committed_is_probed_and_reported_committed()
        {
            using Instruments instruments = new(fixture);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            RecordingConnection connection = new(fixture.Tenant.ConnectionString);

            DataBatch batch = Batch(scope, BatchPurpose.Persist, connection);
            _ = batch.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({ProbedId});", new SqlOptions(writes: Set(Jobs)));
            batch.DeclareInsertedRoots(Jobs, [ProbedId]);
            Ready(batch, async (_, _) =>
            {
                // The server runs on to its COMMIT without the client: once the row is committed,
                // the client loses the connection before it reads the end of the batch.
                await WaitUntilAsync(async () => await JobCountAsync() == 1);
                connection.Current!.Close();
            });
            bool committed = false;
            batch.OnCommitted(_ => committed = true);

            DataAccessAmbiguousException ambiguous = await Assert.ThrowsAsync<DataAccessAmbiguousException>(() => batch.ExecuteAsync(Token));

            // --- The batch committed, but no commit callback runs on an ambiguous outcome ---
            Assert.Equal(CommitVerdict.Committed, ambiguous.Verdict);
            Assert.False(committed);
            Assert.Equal(1, await JobCountAsync());
            Assert.Equal([DataMetrics.ProbeCommitted], instruments.Probes());
            Assert.Empty(instruments.RetryClasses());
        }

        [Fact]
        public async Task The_probe_waits_for_a_round_trip_still_running_and_answers_its_outcome()
        {
            TableTypeBinder binder = fixture.Tenant.Services.GetRequiredService<TableTypeBinder>();
            foreach (bool commits in new[] { true, false })
            {
                await fixture.ResetAsync();
                var probe = CommitProbe.ForIds(Jobs, [ProbedId], binder);

                // --- A session that inserted the probed row and has not committed yet ---
                await using SqlConnection writer = await OpenAsync();
                await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync(Token);
                int writerSession = await ScalarInAsync(writer, transaction, InsertProbedJob + "\nSELECT @@SPID;");

                // --- The probe blocks on the row's lock instead of reading past it ---
                await using SqlConnection prober = await OpenAsync();
                Task<CommitVerdict> verdict = probe.RunAsync(prober, timeoutSeconds: 60);
                await WaitUntilAsync(() => IsBlockedByAsync(writerSession));
                Assert.False(verdict.IsCompleted);

                // --- The writer's outcome is the probe's verdict ---
                if (commits)
                {
                    await transaction.CommitAsync(Token);
                }
                else
                {
                    await transaction.RollbackAsync(Token);
                }

                Assert.Equal(commits ? CommitVerdict.Committed : CommitVerdict.NotCommitted, await verdict);
            }
        }

        [Fact]
        public async Task A_re_run_that_collides_with_the_probed_key_after_a_not_committed_verdict_reports_the_commit()
        {
            await SeedTagsAsync(GateName);
            await using Gate gate = await Gate.CloseAsync(fixture.Tenant.ConnectionString);
            using Instruments instruments = new(fixture);

            // --- Between the not-committed verdict and the re-run, the probed row commits after all ---
            IMeterFactory meters = fixture.Tenant.Services.GetRequiredService<IMeterFactory>();
            using MeterListener listener = new()
            {
                InstrumentPublished = (instrument, listening) =>
                {
                    if (ReferenceEquals(instrument.Meter.Scope, meters) && instrument.Name == DataTelemetryNames.CommitProbes)
                    {
                        listening.EnableMeasurementEvents(instrument);
                    }
                },
            };
            listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    if (tag.Key == DataTelemetryNames.OutcomeTag && Equals(tag.Value, DataMetrics.ProbeNotCommitted))
                    {
                        InsertProbedJobNow();
                    }
                }
            });
            listener.Start();

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            DataBatch batch = Batch(scope, BatchPurpose.Persist);
            _ = batch.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({ProbedId});", new SqlOptions(writes: Set(Jobs)));
            batch.DeclareInsertedRoots(Jobs, [ProbedId]);
            Ready(batch, (_, spid) => KillAsync(spid));
            _ = batch.Sql(GateText, null);

            DataAccessAmbiguousException ambiguous = await Assert.ThrowsAsync<DataAccessAmbiguousException>(() => batch.ExecuteAsync(Token));

            // --- The duplicate key proves the commit; the failure reported is the first attempt's ---
            Assert.Equal(CommitVerdict.Committed, ambiguous.Verdict);
            Assert.IsNotType<UniqueConstraintViolationException>(ambiguous.InnerException);
            Assert.Equal([DataMetrics.ProbeNotCommitted, DataMetrics.ProbeCommitted], instruments.Probes());
            Assert.Equal(1, await JobCountAsync());
        }

        [Fact]
        public async Task A_writing_batch_that_loses_its_connection_with_nothing_to_probe_reports_the_commit_unknown()
        {
            await SeedTagsAsync(GateName);
            await using Gate gate = await Gate.CloseAsync(fixture.Tenant.ConnectionString);
            using Instruments instruments = new(fixture);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();

            DataBatch batch = Batch(scope, BatchPurpose.Persist);
            _ = batch.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({ProbedId});", new SqlOptions(writes: Set(Jobs)));
            Ready(batch, (_, spid) => KillAsync(spid));
            _ = batch.Sql(GateText, null);

            DataAccessAmbiguousException ambiguous = await Assert.ThrowsAsync<DataAccessAmbiguousException>(() => batch.ExecuteAsync(Token));

            Assert.Equal(CommitVerdict.Unknown, ambiguous.Verdict);
            Assert.Equal([DataMetrics.ProbeUnknown], instruments.Probes());
            Assert.Equal(0, await JobCountAsync());
        }

        [Fact]
        public async Task An_explicit_transaction_that_loses_its_connection_is_never_re_run()
        {
            await SeedTagsAsync(GateName);
            await using Gate gate = await Gate.CloseAsync(fixture.Tenant.ConnectionString);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();

            DataBatch batch = Batch(scope, BatchPurpose.Persist);
            batch.TransactionMode = TransactionMode.Explicit;
            _ = batch.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({ProbedId});", new SqlOptions(writes: Set(Jobs)));
            batch.DeclareInsertedRoots(Jobs, [ProbedId]);
            Ready(batch, (_, spid) => KillAsync(spid));
            _ = batch.Sql(GateText, null);

            // The caller's unit of work spans the transaction, so only the caller can re-run it.
            DataAccessRetryExhaustedException exhausted = await Assert.ThrowsAsync<DataAccessRetryExhaustedException>(() => batch.ExecuteAsync(Token));
            Assert.Equal(1, exhausted.Attempts);
            Assert.Equal(0, await JobCountAsync());
        }

        [Fact]
        public async Task A_read_that_loses_its_connection_is_re_run_because_it_may_run_twice()
        {
            await SeedTagsAsync(GateName);
            await using Gate gate = await Gate.CloseAsync(fixture.Tenant.ConnectionString);
            using Instruments instruments = new(fixture);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();

            DataBatch batch = Batch(scope, BatchPurpose.Read);
            BatchResult<RawResult> count = batch.Sql($"SELECT COUNT(*) AS [N] FROM [core].[Jobs];", new SqlOptions(resultSets: 1));
            Ready(batch, async (attempt, spid) =>
            {
                if (attempt == 1)
                {
                    await KillAsync(spid);
                }
                else
                {
                    await gate.OpenAsync();
                }
            });
            _ = batch.Sql(GateText, null);
            BatchResult<RawResult> five = batch.Sql($"SELECT 5 AS [Five];", new SqlOptions(resultSets: 1));

            BatchOutcome outcome = await batch.ExecuteAsync(Token);

            // --- The re-run's results, each completed once; no probe for a read ---
            Assert.Equal((0, 5), (Int(count.Value), Int(five.Value)));
            Assert.Equal((1, 2), (access.Retries, outcome.RoundTrips));
            Assert.Equal([DataMetrics.AmbiguousClass], instruments.RetryClasses());
            Assert.Empty(instruments.Probes());
        }

        [Fact]
        public async Task A_deadlock_victim_is_re_run()
        {
            await SeedTagsAsync("fx-a", "fx-b");
            using Instruments instruments = new(fixture);

            // --- A rival that wins any deadlock holds fx-b ---
            await using SqlConnection rival = await OpenAsync();
            _ = await ScalarInAsync(rival, transaction: null, "SET DEADLOCK_PRIORITY HIGH;\nSELECT 0;");
            await using var rivalTransaction = (SqlTransaction)await rival.BeginTransactionAsync(Token);
            _ = await ScalarInAsync(rival, rivalTransaction, "UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'fx-b';\nSELECT 0;");

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            DataBatch batch = Batch(scope, BatchPurpose.Persist);
            _ = batch.Sql($"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'fx-a';", new SqlOptions(writes: Set(VersionTags)));

            // Once the batch holds fx-a and waits for fx-b, the rival asks for fx-a and commits.
            Task? rivalWrite = null;
            Ready(batch, (attempt, _) =>
            {
                if (attempt == 1)
                {
                    rivalWrite = RivalAsync();
                }

                return Task.CompletedTask;
            });
            _ = batch.Sql($"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'fx-b';", new SqlOptions(writes: Set(VersionTags)));

            _ = await batch.ExecuteAsync(Token);
            Assert.NotNull(rivalWrite);
            await rivalWrite;

            Assert.Equal(1, access.Retries);
            Assert.Equal([(DataMetrics.ReportedClass, "1205")], instruments.Retries());

            async Task RivalAsync()
            {
                _ = await ScalarInAsync(rival, rivalTransaction, "UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'fx-a';\nSELECT 0;");
                await rivalTransaction.CommitAsync(Token);
            }
        }

        /// <summary>A batch of the scope on a connection of the test's.</summary>
        /// <param name="scope">The scope, which supplies the contributors, the counters and the audit.</param>
        /// <param name="purpose">The purpose.</param>
        /// <param name="connection">The connection; a fresh recording one when null.</param>
        /// <returns>The batch.</returns>
        private DataBatch Batch(AsyncServiceScope scope, BatchPurpose purpose, RecordingConnection? connection = null)
        {
            return new DataBatch(
                fixture.Tenant.Services.GetRequiredService<DataExecutor>(),
                connection ?? new RecordingConnection(fixture.Tenant.ConnectionString),
                scope.ServiceProvider,
                purpose);
        }

        /// <summary>Appends the ready statement, whose reader runs the test's action with the attempt and the session.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="onReady">The action, given the attempt from 1 and the batch's session id.</param>
        private static void Ready(DataBatch batch, Func<int, int, Task> onReady)
        {
            int attempts = 0;
            StatementDraft draft = batch.Begin();
            int ordinal = draft.Ordinal;
            batch.Append(
                draft,
                ReadyText,
                reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken);
                    _ = await set.ReadAsync(cancellationToken);
                    int session = Convert.ToInt32(set.GetValue(0), CultureInfo.InvariantCulture);
                    cursor.CountRows(1);
                    await onReady(++attempts, session);
                });
        }

        /// <summary>Ends a session from the server's administrative connection.</summary>
        /// <param name="session">The session id.</param>
        /// <returns>A task that completes when the session is killed.</returns>
        private Task KillAsync(int session)
        {
            return SqlServerFixture.ExecuteAsync(fixture.MasterConnectionString, string.Create(CultureInfo.InvariantCulture, $"KILL {session};"));
        }

        /// <summary>Whether a request waits for a lock the session holds.</summary>
        /// <param name="session">The session id.</param>
        /// <returns>True when one does.</returns>
        private async Task<bool> IsBlockedByAsync(int session)
        {
            return await SqlServerFixture.ScalarAsync<int>(
                fixture.MasterConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE [blocking_session_id] = {session};")) > 0;
        }

        /// <summary>Inserts the probed id at once, outside any batch, from a measurement callback.</summary>
        private void InsertProbedJobNow()
        {
            using SqlConnection side = new(fixture.Tenant.ConnectionString);
            side.Open();
            using SqlCommand insert = side.CreateCommand();
            insert.CommandText = InsertProbedJob;
            _ = insert.ExecuteNonQuery();
        }

        /// <summary>Stores version-tag rows with fresh tags.</summary>
        /// <param name="names">The names.</param>
        /// <returns>A task that completes when they are stored.</returns>
        private Task SeedTagsAsync(params string[] names)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.Tenant.ConnectionString,
                $"INSERT INTO [core].[VersionTags] ([Name]) VALUES {string.Join(", ", names.Select(static name => $"(N'{name}')"))};");
        }

        /// <summary>The job rows, as committed.</summary>
        /// <returns>The count.</returns>
        private async Task<int> JobCountAsync()
        {
            return await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];");
        }

        /// <summary>Opens a side connection to the fixture database.</summary>
        /// <returns>The connection.</returns>
        private async Task<SqlConnection> OpenAsync()
        {
            SqlConnection connection = new(fixture.Tenant.ConnectionString);
            await connection.OpenAsync(Token);
            return connection;
        }

        /// <summary>Runs text on a side connection and reads its first value as an <see cref="int" />.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">Its transaction, if any.</param>
        /// <param name="text">The text, ending in a select of one integer.</param>
        /// <returns>The value.</returns>
        private static async Task<int> ScalarInAsync(SqlConnection connection, SqlTransaction? transaction, string text)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = text;
            return Convert.ToInt32(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
        }

        /// <summary>Polls a condition until it holds.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when it holds.</returns>
        /// <exception cref="TimeoutException">It did not hold within 30 seconds.</exception>
        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The condition did not hold within 30 seconds.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), Token);
            }
        }

        /// <summary>The first column of a raw result's first row, as an <see cref="int" />.</summary>
        /// <param name="result">The result.</param>
        /// <returns>The value.</returns>
        private static int Int(RawResult result)
        {
            return ((int[])result.ResultSets[0].GetBuffer(0))[0];
        }

        /// <summary>A set of tables.</summary>
        /// <param name="tables">The tables.</param>
        /// <returns>The set.</returns>
        private static HashSet<TableName> Set(params TableName[] tables)
        {
            return [.. tables];
        }

        /// <summary>A side transaction holding the gate row's lock until the test opens the gate.</summary>
        private sealed class Gate : IAsyncDisposable
        {
            /// <summary>The side connection.</summary>
            private readonly SqlConnection _connection;

            /// <summary>The transaction holding the lock.</summary>
            private readonly SqlTransaction _transaction;

            /// <summary>Whether the gate is open.</summary>
            private bool _open;

            /// <summary>Creates the gate; see <see cref="CloseAsync" />.</summary>
            /// <param name="connection">The side connection.</param>
            /// <param name="transaction">The transaction holding the lock.</param>
            private Gate(SqlConnection connection, SqlTransaction transaction)
            {
                _connection = connection;
                _transaction = transaction;
            }

            /// <summary>Locks the gate row in a side transaction.</summary>
            /// <param name="connectionString">The fixture database.</param>
            /// <returns>The closed gate.</returns>
            public static async Task<Gate> CloseAsync(string connectionString)
            {
                SqlConnection connection = new(connectionString);
                await connection.OpenAsync(Token);
                var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Token);
                _ = await ScalarInAsync(connection, transaction, "UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'fx-gate';\nSELECT @@ROWCOUNT;");
                return new Gate(connection, transaction);
            }

            /// <summary>Releases the lock, once.</summary>
            /// <returns>A task that completes when it is released.</returns>
            public async Task OpenAsync()
            {
                if (!_open)
                {
                    _open = true;
                    await _transaction.RollbackAsync(Token);
                }
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                await OpenAsync();
                await _transaction.DisposeAsync();
                await _connection.DisposeAsync();
            }
        }

        /// <summary>A batch connection that opens a fresh pooled connection per attempt and keeps the latest.</summary>
        /// <param name="connectionString">The fixture database.</param>
        private sealed class RecordingConnection(string connectionString) : IBatchConnection
        {
            /// <summary>The connection handed out last.</summary>
            public SqlConnection? Current { get; private set; }

            /// <inheritdoc />
            public int TenantId => FixtureTenant.TenantId;

            /// <inheritdoc />
            public async ValueTask<SqlConnection> AcquireAsync(CancellationToken cancellationToken)
            {
                SqlConnection connection = new(connectionString);
                await connection.OpenAsync(cancellationToken);
                Current = connection;
                return connection;
            }

            /// <inheritdoc />
            public async ValueTask ReleaseAsync(SqlConnection connection)
            {
                await connection.DisposeAsync();
            }
        }

        /// <summary>The retry and probe instruments of the class's host, from the test's start.</summary>
        private sealed class Instruments : IDisposable
        {
            /// <summary>The re-runs.</summary>
            private readonly MetricCollector<long> _retries;

            /// <summary>The commit verdicts.</summary>
            private readonly MetricCollector<long> _probes;

            /// <summary>Starts collecting.</summary>
            /// <param name="fixture">The class's database, whose host the instruments belong to.</param>
            public Instruments(DataBatchDatabase fixture)
            {
                IMeterFactory meters = fixture.Tenant.Services.GetRequiredService<IMeterFactory>();
                _retries = new MetricCollector<long>(meters, DataTelemetryNames.MeterName, DataTelemetryNames.Retries);
                _probes = new MetricCollector<long>(meters, DataTelemetryNames.MeterName, DataTelemetryNames.CommitProbes);
            }

            /// <summary>The verdicts recorded, in order.</summary>
            /// <returns>Each verdict's outcome tag.</returns>
            public string[] Probes()
            {
                return [.. _probes.GetMeasurementSnapshot().Select(static measurement => Tag(measurement, DataTelemetryNames.OutcomeTag))];
            }

            /// <summary>The re-runs' classes, in order.</summary>
            /// <returns>Each re-run's class tag.</returns>
            public string[] RetryClasses()
            {
                return [.. Retries().Select(static retry => retry.Class)];
            }

            /// <summary>The re-runs, in order.</summary>
            /// <returns>Each re-run's class and error tags.</returns>
            public (string Class, string Error)[] Retries()
            {
                return [.. _retries.GetMeasurementSnapshot().Select(static measurement => (Tag(measurement, DataTelemetryNames.ClassTag), Tag(measurement, DataTelemetryNames.ErrorTag)))];
            }

            /// <summary>A measurement's string tag, which every recording of these instruments carries.</summary>
            /// <param name="measurement">The measurement.</param>
            /// <param name="name">The tag's name.</param>
            /// <returns>The tag's value.</returns>
            private static string Tag(CollectedMeasurement<long> measurement, string name)
            {
                return Assert.IsType<string>(measurement.Tags[name]);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _retries.Dispose();
                _probes.Dispose();
            }
        }
    }
}
