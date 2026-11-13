// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Queries;
using Tellma.Core.Data.Saves;
using Tellma.Core.Data.TableTypes;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     Batches over the default composition's model with no database: an executor whose stack
    ///     owners the test chooses, scopes holding the test's contributors, and a connection that
    ///     refuses, so executing a batch runs its stages and assembles it, then stops before sending.
    /// </summary>
    internal sealed class BatchHarness : IDisposable
    {
        /// <summary>The composed root.</summary>
        private readonly ServiceProvider _root;

        /// <summary>Composes the default composition.</summary>
        /// <param name="owners">Which stack owns each table; none when null.</param>
        /// <param name="registry">Which tags written tables bump; none composed when null.</param>
        /// <param name="compose">Composes the model's features; the default composition when null.</param>
        public BatchHarness(IStackTableOwners? owners = null, IVersionTagRegistry? registry = null, Action<TellmaBuilder>? compose = null)
        {
            IServiceCollection services = TestComposition.Compose(compose ?? (static _ => { }));
            if (registry is not null)
            {
                _ = services.AddSingleton(registry);
            }

            _root = services.BuildServiceProvider(validateScopes: true);
            Executor = new DataExecutor(
                _root.GetRequiredService<IOptions<DataOptions>>(),
                _root.GetRequiredService<IEntityMetadataProvider>(),
                _root.GetRequiredService<TableTypeBinder>(),
                owners ?? new NoStackTableOwners(),
                TimeProvider.System,
                _root.GetRequiredService<ILogger<DataBatch>>(),
                _root.GetRequiredService<DataMetrics>(),
                _root.GetRequiredService<QueryServices>(),
                _root.GetRequiredService<IdAllocator>(),
                _root.GetRequiredService<SaveEmitter>(),
                _root);
        }

        /// <summary>The executor.</summary>
        public DataExecutor Executor { get; }

        /// <summary>The multilingual shape the batches' connection reports; one language by default.</summary>
        public MultilingualShape Shape { get; set; } = MultilingualShape.Primary;

        /// <summary>The context values the batches' connection reports.</summary>
        public QueryexContextValues Context { get; set; } = new(
            new DateOnly(2026, 3, 14), new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.FromHours(3)), 7, "Arab Standard Time");

        /// <summary>The composed root, for the services a test resolves.</summary>
        public IServiceProvider Services => _root;

        /// <summary>Creates a batch whose scope holds the contributors.</summary>
        /// <param name="purpose">The purpose.</param>
        /// <param name="contributors">The scope's contributors, in registration order.</param>
        /// <returns>The batch.</returns>
        public DataBatch Batch(BatchPurpose purpose = BatchPurpose.Read, params IDataBatchContributor[] contributors)
        {
            ServiceCollection scope = [];
            foreach (IDataBatchContributor contributor in contributors)
            {
                _ = scope.AddSingleton(contributor);
            }

            return new DataBatch(Executor, new RefusedConnection(Shape, Context), scope.BuildServiceProvider(), purpose);
        }

        /// <summary>Executes a batch up to the send, which the connection refuses.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The batch's assembly, from its sealed statements.</returns>
        public static async Task<BatchAssembly> SealAsync(DataBatch batch)
        {
            _ = await Assert.ThrowsAsync<RefusedConnection.RefusedException>(() => batch.ExecuteAsync(TestContext.Current.CancellationToken));
            return batch.Assemble();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _root.Dispose();
        }

        /// <summary>A connection that is never opened.</summary>
        /// <param name="shape">The multilingual shape it reports.</param>
        /// <param name="context">The context values it reports.</param>
        private sealed class RefusedConnection(MultilingualShape shape, QueryexContextValues context) : IBatchConnection
        {
            /// <inheritdoc />
            public int TenantId => 1;

            /// <inheritdoc />
            public MultilingualShape Shape => shape;

            /// <inheritdoc />
            public QueryexContextValues Context => context;

            /// <inheritdoc />
            public int? UserId => 7;

            /// <inheritdoc />
            public ValueTask<SqlConnection> AcquireAsync(CancellationToken cancellationToken)
            {
                throw new RefusedException();
            }

            /// <inheritdoc />
            public ValueTask ReleaseAsync(SqlConnection connection)
            {
                return ValueTask.CompletedTask;
            }

            /// <summary>The refusal, distinct from anything the batch itself throws.</summary>
            internal sealed class RefusedException : Exception;
        }
    }

    /// <summary>A contributor that appends one statement per stage, as text the test chooses.</summary>
    /// <param name="order">The contributor's order.</param>
    /// <param name="prologue">The prologue text, or null for none.</param>
    /// <param name="epilogue">The epilogue text, or null for none.</param>
    internal sealed class TextContributor(int order, string? prologue, string? epilogue) : IDataBatchContributor
    {
        /// <summary>The stages the contributor ran, in order.</summary>
        public List<DataBatchStage> Ran { get; } = [];

        /// <inheritdoc />
        public int Order => order;

        /// <inheritdoc />
        public void Contribute(IDataBatch batch, DataBatchStage stage)
        {
            Ran.Add(stage);
            string? text = stage == DataBatchStage.Prologue ? prologue : epilogue;
            if (text is not null && batch is DataBatch data)
            {
                data.Append(data.Begin(), text);
            }
        }
    }

    /// <summary>A contributor that runs the test's action at each stage.</summary>
    /// <param name="order">The contributor's order.</param>
    /// <param name="contribute">The action, given the batch and the stage.</param>
    internal sealed class DelegateContributor(int order, Action<IDataBatch, DataBatchStage> contribute) : IDataBatchContributor
    {
        /// <inheritdoc />
        public int Order => order;

        /// <inheritdoc />
        public void Contribute(IDataBatch batch, DataBatchStage stage)
        {
            contribute(batch, stage);
        }
    }

    /// <summary>Stack owners from a fixed map.</summary>
    /// <param name="owners">Each owned table and its stack.</param>
    internal sealed class MapOwners(Dictionary<TableName, string> owners) : IStackTableOwners
    {
        /// <inheritdoc />
        public string? OwnerOf(TableName table)
        {
            return owners.GetValueOrDefault(table);
        }
    }
}
