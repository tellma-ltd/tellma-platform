// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;
using Tellma.Core.Data.Batches;
using Tellma.Core.Excel;
using Tellma.Core.IntegrationTests.Crud;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Jobs;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;
using Tellma.Core.Testing.Blobs;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Excel
{
    /// <summary>What a test does on each validation of a node save.</summary>
    public sealed class NodeSaveHook
    {
        /// <summary>Called with the nodes of each validation; nothing when null.</summary>
        public Func<IReadOnlyList<Node>, Task>? Validating { get; set; }
    }

    /// <summary>
    ///     The fixture database of the background exports and imports: one tenant over the fixture
    ///     model with in-memory blobs, the jobs' machinery polled on demand, recorded notices, a
    ///     widget save probe and a warning an import can leave unconfirmed, and imports chunked in
    ///     two parent rows so a few rows span several chunks.
    /// </summary>
    /// <param name="sql">The SQL Server fixture.</param>
    public sealed class ExcelJobsDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>A member who reads and saves widgets and nodes, and runs every export and import.</summary>
        public const int ImporterId = 301;

        /// <summary>A member who only reads widgets.</summary>
        public const int ReaderId = 311;

        /// <summary>A member whose save of widgets is filtered to the code <c>OK-1</c>.</summary>
        public const int FilteredId = 321;

        /// <summary>A member who reads widgets until a test revokes it; no other test runs as them.</summary>
        public const int RevokedId = 331;

        /// <summary>The parent rows a chunk holds unless a test changes it.</summary>
        public const int ChunkRows = 2;

        /// <summary>The widget name the warning validator asks to confirm.</summary>
        public const string WarnedName = "Confirm me";

        /// <summary>The warning's key.</summary>
        public const string WarningKey = "Test.ImportChecked";

        /// <summary>The statements that clear what a test wrote.</summary>
        private const string ResetText = """
            DELETE FROM [core].[Imports];
            DELETE FROM [core].[Exports];
            DELETE FROM [core].[Jobs];
            DELETE FROM [core].[Blobs];
            DELETE FROM [fixture].[WidgetPartNotes];
            DELETE FROM [fixture].[WidgetParts];
            DELETE FROM [fixture].[Widgets];
            DELETE FROM [fixture].[Links];
            DELETE FROM [fixture].[Nodes];
            """;

        /// <summary>The fixture tenant; null until created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The fixture tenant.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The tenant database's connection string.</summary>
        public string ConnectionString => Tenant.ConnectionString;

        /// <summary>The notices the jobs raised, as each commit recorded them.</summary>
        public CommittedNotices Notices { get; } = new();

        /// <summary>The widget save probe.</summary>
        public WidgetSaveProbe Probe { get; } = new();

        /// <summary>What each validation of a node save does besides; nothing when null.</summary>
        public NodeSaveHook NodeHook { get; } = new();

        /// <summary>The codec's options, which a test may change and <see cref="ResetAsync" /> puts back.</summary>
        public ExcelOptions Options => Tenant.Services.GetRequiredService<IOptions<ExcelOptions>>().Value;

        /// <summary>The records the composition logged.</summary>
        public FakeLogCollector Log => Tenant.Services.GetFakeLogCollector();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_exceljobs"),
                services =>
                {
                    _ = services.AddInMemoryBlobs();
                    _ = services.AddSingleton(Probe);
                    _ = services.AddSingleton(NodeHook);
                    _ = services.Replace(ServiceDescriptor.Scoped<INotifier>(_ => Notices));
                    _ = services.AddFakeLogging();
                    _ = services.Configure<ExcelOptions>(static options => options.ImportChunkRows = ChunkRows);

                    // The stacks' tables are theirs again, as in production.
                    _ = services.Replace(ServiceDescriptor.Singleton<IStackTableOwners>(static provider => new StackTableOwners(provider.GetRequiredService<StackRegistry>())));
                },
                // The change-tracking audit is off for this suite's batches, the export and import
                // completions and the chunk saves among them. A handler's jump in progress renews its
                // lease on a second connection while a batch runs, and the audit, which compares the
                // tables that changed during the batch, would charge that renewal to the batch as an
                // undeclared write. Progress.Append's fenced write, which rides every chunk save here,
                // stays audited in the job runner's own suite, whose database suspends the audit only
                // in the tests that run batches side by side.
                audit: false,
                compose: static tellma => tellma.AddFeature(new ImportWarningFeature()),
                cancellationToken: TestContext.Current.CancellationToken);

            // --- The tenant's settings and version tags, the members and their roles ---
            string tags = string.Join(", ", Tenant.Services.GetRequiredService<IVersionTagRegistry>().Names.Select(static name => $"(N'{name}')"));
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Settings] SET [TenantId] = {FixtureTenant.TenantId}, [Name] = N'Fixture';"),
                $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Name], NEWID() FROM (VALUES {tags}) AS n ([Name]) WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Name]);",
                UserInsert(ImporterId),
                UserInsert(ReaderId),
                UserInsert(FilteredId),
                UserInsert(RevokedId),
                RoleInsert("Widget importers", [ImporterId], "fixture.Widget", [AccessActions.Read, AccessActions.Save], filter: null),
                RoleInsert("Node importers", [ImporterId], "fixture.Node", [AccessActions.Read, AccessActions.Save], filter: null),
                RoleInsert("Widget readers", [ReaderId, FilteredId, RevokedId], "fixture.Widget", [AccessActions.Read], filter: null),
                RoleInsert("Filtered savers", [FilteredId], "fixture.Widget", [AccessActions.Save], filter: "Code = 'OK-1'"));
        }

        /// <summary>Clears what a test wrote and puts the options, the notices, the probe's arms and the log back.</summary>
        /// <returns>A task that completes once the database is clear.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(ConnectionString, ResetText);
            ExcelOptions defaults = new();
            Options.ImportChunkRows = ChunkRows;
            Options.MaxAtomicImportRows = defaults.MaxAtomicImportRows;
            Options.MaxExportRows = defaults.MaxExportRows;
            Notices.Clear();
            Probe.Clear();
            NodeHook.Validating = null;
            Log.Clear();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>A scope bound as a member's request: the tenant, the member's subject and a fresh step-up, then the request's initializers.</summary>
        /// <param name="userId">The member.</param>
        /// <param name="state">The tenant's state the request sees; the tenant's own when null.</param>
        /// <returns>The scope, which the caller disposes.</returns>
        public async Task<AsyncServiceScope> UserScopeAsync(int userId, TenantState? state = null)
        {
            AsyncServiceScope scope = Tenant.Services.CreateAsyncScope();
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
                TenantDescriptor tenant = Tenant.Services.GetRequiredService<ITenantRegistry>().Get(FixtureTenant.TenantId).Descriptor;
                RequestContext context = holder.Current with
                {
                    Tenant = tenant with { State = state ?? tenant.State },
                    Kind = PrincipalKind.User,
                    Subject = Subject(userId),
                    UserId = null,
                    Assurance = new AuthenticationAssurance(AssuranceTiers.Aal2, now, now),
                };
                holder.Set(context);
                _ = await RequestContextInitialization.RunAsync(
                    holder,
                    scope.ServiceProvider.GetServices<IRequestContextInitializer>(),
                    context,
                    new RequestContextInputs(null, null, null, null, null, null, SuppressActivityStamp: true),
                    TestContext.Current.CancellationToken);
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }

        /// <summary>Stages a workbook as a member's upload of an import file, its bytes in the store.</summary>
        /// <param name="userId">The uploader.</param>
        /// <param name="bytes">The workbook.</param>
        /// <returns>The upload's id.</returns>
        public async Task<int> StageAsync(int userId, byte[] bytes)
        {
            string storageKey = BlobName.Primary(Import.FileKind, Guid.NewGuid().ToString("N"));
            int id = await SqlServerFixture.ScalarAsync<int>(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Blobs];
                INSERT INTO [core].[Blobs] ([Id], [Kind], [State], [ContentType], [FileName], [Size], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt], [Sha256], [StorageKey])
                VALUES (@id, N'{Import.FileKind}', 'Staged', N'{BlobContentTypes.Xlsx}', N'import.xlsx', {bytes.Length}, N'', SYSUTCDATETIME(), {userId}, NULL, DATEADD(hour, 1, SYSUTCDATETIME()), 0x{new string('0', 64)}, N'{storageKey}');
                SELECT @id;
                """));

            IBlobStore store = Tenant.Services.GetRequiredService<IBlobStore>();
            await store.EnsureTenantAsync(FixtureTenant.TenantId, TestContext.Current.CancellationToken);
            using MemoryStream content = new(bytes, writable: false);
            await store.WriteAsync(FixtureTenant.TenantId, [new BlobWrite(storageKey, BlobContentTypes.Xlsx, content, bytes.Length)], TestContext.Current.CancellationToken);
            return id;
        }

        /// <summary>A stored blob's bytes.</summary>
        /// <param name="id">The blob's id.</param>
        /// <returns>The bytes.</returns>
        public async Task<byte[]> BlobBytesAsync(int id)
        {
            string key = (await SqlServerFixture.ScalarAsync<string>(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT [StorageKey] FROM [core].[Blobs] WHERE [Id] = {id};")))!;
            BlobContent? content = await Tenant.Services.GetRequiredService<IBlobStore>().OpenReadAsync(FixtureTenant.TenantId, key, TestContext.Current.CancellationToken);
            Assert.NotNull(content);
            await using (content)
            {
                using MemoryStream bytes = new();
                await content.Content.CopyToAsync(bytes, TestContext.Current.CancellationToken);
                return bytes.ToArray();
            }
        }

        /// <summary>Runs one poll of the export and import handlers, as a worker's would.</summary>
        /// <returns>The jobs the poll ran.</returns>
        public Task<IReadOnlyList<PolledJob>> PollAsync()
        {
            return PollStoppingAsync(CancellationToken.None);
        }

        /// <summary>Runs one poll of the export and import handlers under a host that may stop while it runs.</summary>
        /// <param name="hostToken">The host's stopping token.</param>
        /// <returns>The jobs the poll ran.</returns>
        public Task<IReadOnlyList<PolledJob>> PollStoppingAsync(CancellationToken hostToken)
        {
            return JobPollDriver.PollOnceAsync(Tenant, [ExcelJobs.ExportHandlerKey, ExcelJobs.ImportHandlerKey], hostToken, TestContext.Current.CancellationToken);
        }

        /// <summary>Makes a job due again now, as its retry delay ran out.</summary>
        /// <param name="jobId">The job.</param>
        /// <returns>A task that completes once the job is due.</returns>
        public Task DueNowAsync(int jobId)
        {
            return SqlServerFixture.ExecuteAsync(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET [DueAt] = DATEADD(second, -1, SYSUTCDATETIME()) WHERE [Id] = {jobId};"));
        }

        /// <summary>Renews the tenant's leases now, as the worker's timer would, so a running handler reads a cancel requested since its claim.</summary>
        /// <returns>A task that completes once the renewal ran.</returns>
        public Task RenewAsync()
        {
            return Tenant.Services.GetRequiredService<IJobRenewal>().RenewAsync(FixtureTenant.TenantId, TestContext.Current.CancellationToken);
        }

        /// <summary>Sets columns of a stored job row, as the API or an operator would.</summary>
        /// <param name="jobId">The job.</param>
        /// <param name="assignments">The assignments of the statement's SET clause.</param>
        /// <returns>A task that completes once the row is changed.</returns>
        public Task SetJobAsync(int jobId, string assignments)
        {
            return SqlServerFixture.ExecuteAsync(ConnectionString, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET {assignments} WHERE [Id] = {jobId};"));
        }

        /// <summary>Revokes every role of a member and moves the permission tags, so the next check reads the revocation.</summary>
        /// <param name="userId">The member.</param>
        /// <returns>A task that completes once the roles are gone.</returns>
        public Task RevokeAsync(int userId)
        {
            return SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"DELETE FROM [core].[RoleMemberships] WHERE [UserId] = {userId}; UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'permissions'; UPDATE [core].[UserStamps] SET [PermissionsTag] = NEWID() WHERE [UserId] = {userId};"));
        }

        /// <summary>A text read from the tenant database.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The text; null for none.</returns>
        public Task<string?> TextAsync(string query)
        {
            return SqlServerFixture.ScalarAsync<string>(ConnectionString, query);
        }

        /// <summary>A stored job row's columns.</summary>
        /// <param name="id">The job.</param>
        /// <returns>The row.</returns>
        public Task<JobRow> JobAsync(int id)
        {
            return RowAsync(id, "[core].[Jobs]", static row => new JobRow(row));
        }

        /// <summary>A stored row's columns by name.</summary>
        /// <param name="table">The table.</param>
        /// <param name="where">The condition naming one row.</param>
        /// <returns>The columns.</returns>
        public async Task<Dictionary<string, object?>> RowAsync(string table, string where)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} WHERE {where};";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), $"A row of {table} matches {where}.");
            Dictionary<string, object?> row = new(StringComparer.Ordinal);
            for (int column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            }

            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken), $"One row of {table} matches {where}.");
            return row;
        }

        /// <summary>A count read from the tenant database.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The count.</returns>
        public Task<int> CountAsync(string query)
        {
            return SqlServerFixture.ScalarAsync<int>(ConnectionString, query);
        }

        /// <summary>A stored row of a table by id, mapped.</summary>
        /// <typeparam name="T">The mapped row.</typeparam>
        /// <param name="id">The id.</param>
        /// <param name="table">The table.</param>
        /// <param name="map">Maps the columns.</param>
        /// <returns>The row.</returns>
        private async Task<T> RowAsync<T>(int id, string table, Func<Dictionary<string, object?>, T> map)
        {
            return map(await RowAsync(table, string.Create(CultureInfo.InvariantCulture, $"[Id] = {id}")));
        }

        /// <summary>A member's subject.</summary>
        /// <param name="id">The member's id.</param>
        /// <returns>The subject.</returns>
        private static string Subject(int id)
        {
            return string.Create(CultureInfo.InvariantCulture, $"fx-exceljobs-{id}");
        }

        /// <summary>The statements that insert an active individual member with a stamp row.</summary>
        /// <param name="id">The member's id.</param>
        /// <returns>The statements.</returns>
        private static string UserInsert(int id)
        {
            string subject = Subject(id);
            int system = WellKnownIds.SystemUserId;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"""
                INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES ({id}, '{UserKind.Individual}', '{subject}', N'{subject}@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{subject}', 1, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                INSERT INTO [core].[UserStamps] ([UserId]) VALUES ({id});
                """);
        }

        /// <summary>The statements that insert a role over one resource, its permissions and its members.</summary>
        /// <param name="name">The role's name.</param>
        /// <param name="members">The role's members.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="actions">The actions it grants.</param>
        /// <param name="filter">The filter every permission carries; null for none.</param>
        /// <returns>The statements.</returns>
        private static string RoleInsert(string name, IReadOnlyList<int> members, string resource, IReadOnlyList<string> actions, string? filter)
        {
            int system = WellKnownIds.SystemUserId;
            string text = filter is null ? "NULL" : $"N'{filter.Replace("'", "''", StringComparison.Ordinal)}'";
            string version = filter is null ? "NULL" : QueryexLanguage.Version.ToString(CultureInfo.InvariantCulture);
            StringBuilder sql = new();
            _ = sql.Append(CultureInfo.InvariantCulture, $"""
                DECLARE @role int = NEXT VALUE FOR [core].[sq_Roles];
                INSERT INTO [core].[Roles] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (@role, N'{name}', SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});

                """);

            // --- One permission per action, then one membership per member ---
            foreach (string action in actions)
            {
                _ = sql.Append(CultureInfo.InvariantCulture, $"""
                    INSERT INTO [core].[Permissions] ([Id], [RoleId], [Resource], [Action], [Filter], [FilterQueryexVersion]) VALUES (NEXT VALUE FOR [core].[sq_Permissions], @role, N'{resource}', N'{action}', {text}, {version});

                    """);
            }

            foreach (int member in members)
            {
                _ = sql.Append(CultureInfo.InvariantCulture, $"""
                    INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId]) VALUES (NEXT VALUE FOR [core].[sq_RoleMemberships], {member}, @role);

                    """);
            }

            return sql.ToString();
        }

        /// <summary>The fixture's feature: the probe's validator, and a warning on a widget named to be confirmed.</summary>
        private sealed class ImportWarningFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "excel-jobs-probe";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution
                    .Confirmation(WarningKey)
                    .Validator<Widget, WidgetSaveProbeValidator>()
                    .Validator<Widget, ImportWarningValidator>()
                    .Validator<Node, NodeHookValidator>();
            }
        }

        /// <summary>Runs the node hook on each validation of a node save.</summary>
        /// <param name="hook">The hook.</param>
        private sealed class NodeHookValidator(NodeSaveHook hook) : IEntityValidator<Node>
        {
            /// <inheritdoc />
            public Task ValidateAsync(SaveContext<Node> context)
            {
                ArgumentNullException.ThrowIfNull(context);
                return hook.Validating?.Invoke(context.Entities) ?? Task.CompletedTask;
            }
        }

        /// <summary>Asks to confirm the save of every widget named <see cref="WarnedName" />.</summary>
        private sealed class ImportWarningValidator : IEntityValidator<Widget>
        {
            /// <inheritdoc />
            public Task ValidateAsync(SaveContext<Widget> context)
            {
                ArgumentNullException.ThrowIfNull(context);
                long[] warned = [.. context.Entities.Where(static widget => widget.Name == WarnedName).Select(static widget => (long)widget.Id)];
                if (warned.Length > 0)
                {
                    context.Host.Confirmations.Require(WarningKey, warned, new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = warned.Length });
                }

                return Task.CompletedTask;
            }
        }
    }
}
