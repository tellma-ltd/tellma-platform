// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Numerics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Crud;

namespace Tellma.Core.AspNetCore.Tests.Api
{
    // What stands behind the projected endpoints in this suite: a pipeline, an action invoker and a
    // child query service that record each call, witness the securable the real ones evaluate, and
    // answer a canned result, so a test sees exactly what the web layer bound.

    /// <summary>One call that reached the recording stand-ins.</summary>
    /// <param name="Operation">The operation: a pipeline operation's name, <c>action:</c> or <c>api:</c> and the action's name, or <c>child-query</c>.</param>
    /// <param name="Ids">The ids the call carried, as the key type's values.</param>
    /// <param name="Arguments">The action's arguments, the API action's body, or the request.</param>
    /// <param name="Options">The save's, action's or details' options.</param>
    /// <param name="Confirmed">The confirmation keys the scope held when the call ran.</param>
    internal sealed record PipelineCall(string Operation, IReadOnlyList<object> Ids, object? Arguments, object? Options, IReadOnlyCollection<string> Confirmed);

    /// <summary>The calls the stand-ins recorded, shared by a host's scopes.</summary>
    internal sealed class PipelineLog
    {
        private readonly ConcurrentQueue<PipelineCall> _calls = new();
        private readonly ConcurrentQueue<ObservedStream> _workbooks = new();

        /// <summary>The non-ASCII file name of the workbook the inbox's export answers.</summary>
        public const string WorkbookName = "Résumé-2026.xlsx";

        /// <summary>The calls, in arrival order.</summary>
        public IReadOnlyList<PipelineCall> Calls => [.. _calls];

        /// <summary>
        ///     The pre-serialized document the inbox's document action answers: PascalCase, which the
        ///     host's camel-case JSON options would never write, so its bytes prove they were not re-serialized.
        /// </summary>
        public static ReadOnlySpan<byte> Document => /*lang=json,strict*/ """{"Unread":3}"""u8;

        /// <summary>The bytes of the workbook the inbox's export answers.</summary>
        public static ReadOnlySpan<byte> WorkbookBytes => "PK workbook bytes"u8;

        /// <summary>The workbook streams the export answered, in order.</summary>
        public IReadOnlyList<ObservedStream> Workbooks => [.. _workbooks];

        /// <summary>Whether the stand-ins skip the witness, as a handler that evaluated nothing would.</summary>
        public bool SkipWitness { get; set; }

        /// <summary>The exception every stand-in throws once it recorded its call, as a refusing pipeline would; null to answer.</summary>
        public Exception? Refusal { get; set; }

        /// <summary>Records a call.</summary>
        /// <param name="call">The call.</param>
        public void Add(PipelineCall call)
        {
            _calls.Enqueue(call);
        }

        /// <summary>A new workbook over <see cref="WorkbookBytes" />, whose stream the log keeps to observe its disposal.</summary>
        /// <returns>The workbook.</returns>
        public ExportOutcome Workbook()
        {
            ObservedStream stream = new(WorkbookBytes.ToArray());
            _workbooks.Enqueue(stream);
            return new ExportOutcome(stream, WorkbookName, Rows: 1);
        }

        /// <summary>The canned result of an operation answering its own result.</summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <returns>The result.</returns>
        public static TResult Result<TResult>()
        {
            return (TResult)Result(typeof(TResult))!;
        }

        /// <summary>The canned result of an operation answering its own result.</summary>
        /// <param name="type">The result type.</param>
        /// <returns>The result; null for none.</returns>
        public static object? Result(Type type)
        {
            return type switch
            {
                _ when type == typeof(JobAccepted) => new JobAccepted(42, null),
                _ when type == typeof(string) => "done",
                _ when type == typeof(int) => 7,
                _ when type == typeof(PjInboxSummary) => new PjInboxSummary(3),
                _ when type == typeof(ValueTuple<byte[], string>) => (Document.ToArray(), "1.document"),

                // An object innermost: one level past what the wire writes.
                _ when type == typeof(PjDepth) => PjDepth.Parse("{}"),
                _ => null,
            };
        }

        /// <summary>An empty row set.</summary>
        /// <returns>The row set.</returns>
        public static QueryRowSet NoRows()
        {
            return new QueryRowSet.Builder([], [], 0).Build();
        }
    }

    /// <summary>The recording pipeline's factory, which the suite's services take in place of the platform's.</summary>
    /// <param name="services">The request scope's services.</param>
    public sealed class RecordingPipelineFactory(IServiceProvider services) : IEntityPipelineFactory
    {
        /// <inheritdoc />
        public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            StackDescriptor descriptor = services.GetRequiredService<StackRegistry>().FindByLeaf(typeof(TEntity))
                ?? throw new InvalidOperationException($"No stack serves {typeof(TEntity).Name}.");
            return new RecordingPipeline<TEntity, TKey>(descriptor, services);
        }
    }

    /// <summary>A pipeline that records each operation, witnesses its securable and answers a canned result.</summary>
    /// <typeparam name="TEntity">The stack's entity leaf.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="descriptor">The stack.</param>
    /// <param name="services">The request scope's services.</param>
    internal sealed class RecordingPipeline<TEntity, TKey>(StackDescriptor descriptor, IServiceProvider services) : IEntityPipeline<TEntity, TKey>
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <inheritdoc />
        public StackDescriptor Descriptor { get; } = descriptor;

        /// <inheritdoc />
        public Task<RowPage> QueryAsync(QueryRequest request, CancellationToken cancellationToken)
        {
            Record("query", AccessActions.Read, [], request, null);
            return Task.FromResult(new RowPage(PipelineLog.NoRows(), Count: null, Ancestors: null, CountCap: null));
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request, CancellationToken cancellationToken)
        {
            Record("get", AccessActions.Read, [id], null, request);
            return Task.FromResult(Entities([id]));
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken)
        {
            Record("get-by-ids", AccessActions.Read, ids, null, request);
            return Task.FromResult(Entities(ids));
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request, CancellationToken cancellationToken)
        {
            Record("all", AccessActions.Read, [], null, request);
            return Task.FromResult(Entities([]));
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken)
        {
            Record("save", AccessActions.Save, [], entities, options);
            return Task.FromResult(Entities([]));
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByIdsAsync(IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps, CancellationToken cancellationToken)
        {
            Record("delete", AccessActions.Delete, ids, expectedStamps, null);
            return Task.FromResult(new AffectedResult(ids.Count));
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken)
        {
            Record("delete-by-query", AccessActions.Delete, [], request, null);
            return Task.FromResult(new AffectedResult(0));
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> ExecuteActionAsync(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            Record("action:" + action, SecurableAction(action), ids, arguments, options);
            return Task.FromResult(Entities(ids));
        }

        /// <inheritdoc />
        public Task<TResult> ExecuteActionAsync<TResult>(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            Record("action:" + action, SecurableAction(action), ids, arguments, options);
            return Task.FromResult(PipelineLog.Result<TResult>());
        }

        /// <inheritdoc />
        public Task<QueryRowSet> GetByParentIdsAsync(ParentIdsRequest request, CancellationToken cancellationToken)
        {
            Record("get-by-parent-ids", AccessActions.Read, [], request, null);
            return Task.FromResult(PipelineLog.NoRows());
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteWithDescendantsAsync(IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
        {
            Record("delete-with-descendants", AccessActions.Delete, ids, null, null);
            return Task.FromResult(new AffectedResult(ids.Count));
        }

        /// <inheritdoc />
        public IExcelRowSource CreateExcelRowSource(ExportSource source)
        {
            throw new NotSupportedException("The projection suite exports nothing.");
        }

        /// <summary>The entities' envelope of the ids, with no rows.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The envelope.</returns>
        private static EntitiesResult<TEntity> Entities(IReadOnlyList<TKey> ids)
        {
            return new EntitiesResult<TEntity> { Ids = [.. ids.Select(static id => long.CreateChecked(id))], Entities = [] };
        }

        /// <summary>The securable action an entity action evaluates.</summary>
        /// <param name="action">The action's name.</param>
        /// <returns>The securable action.</returns>
        private string SecurableAction(string action)
        {
            return Descriptor.Actions.OfType<EntityActionDescriptor>().Single(candidate => candidate.Name == action).Action;
        }

        /// <summary>Records a call and witnesses its securable.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="securable">The securable action on the stack's resource.</param>
        /// <param name="ids">The ids.</param>
        /// <param name="arguments">The arguments or request.</param>
        /// <param name="options">The options.</param>
        private void Record(string operation, string securable, IReadOnlyList<TKey> ids, object? arguments, object? options)
        {
            RecordingCalls.Record(services, operation, Descriptor.Resource, securable, [.. ids.Cast<object>()], arguments, options);
        }
    }

    /// <summary>An action invoker that records each call, witnesses its securable and answers a canned result.</summary>
    /// <param name="services">The request scope's services.</param>
    internal sealed class RecordingApiActionInvoker(IServiceProvider services) : IApiActionInvoker
    {
        /// <inheritdoc />
        public Task<object?> InvokeAsync(ApiActionDescriptor action, object? body, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);

            // A member-only action's witness is the connection's, which already ran.
            RecordingCalls.Record(services, "api:" + action.Name, action.Securable?.Resource, action.Securable?.Action, [], body, null);
            return Task.FromResult(action.ResultType == typeof(ExportOutcome)
                ? services.GetRequiredService<PipelineLog>().Workbook()
                : PipelineLog.Result(action.ResultType));
        }
    }

    /// <summary>A child query service that records each call, witnesses the surface's read and answers no rows.</summary>
    /// <param name="services">The request scope's services.</param>
    internal sealed class RecordingChildQueryService(IServiceProvider services) : IChildQueryService
    {
        /// <inheritdoc />
        public Task<RowPage> QueryAsync(string resource, QueryRequest request, CancellationToken cancellationToken = default)
        {
            RecordingCalls.Record(services, "child-query:" + resource, resource, AccessActions.Read, [], request, null);
            return Task.FromResult(new RowPage(PipelineLog.NoRows(), Count: null, Ancestors: null, CountCap: null));
        }

        /// <inheritdoc />
        public IExcelRowSource ExcelRowSource(string resource, ExportSource source)
        {
            throw new NotSupportedException("The projection suite exports nothing.");
        }
    }

    /// <summary>A read-only stream over fixed bytes that signals its disposal.</summary>
    /// <param name="content">The bytes.</param>
    internal sealed class ObservedStream(byte[] content) : MemoryStream(content, writable: false)
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the stream is disposed.</summary>
        public Task Disposed => _disposed.Task;

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            _ = _disposed.TrySetResult();
            base.Dispose(disposing);
        }
    }

    /// <summary>What every stand-in does on a call.</summary>
    internal static class RecordingCalls
    {
        /// <summary>Records a call with the scope's confirmed keys, and witnesses its securable unless the log says not to.</summary>
        /// <param name="services">The request scope's services.</param>
        /// <param name="operation">The operation.</param>
        /// <param name="resource">The securable's resource; null for none.</param>
        /// <param name="action">The securable's action; null for none.</param>
        /// <param name="ids">The ids.</param>
        /// <param name="arguments">The arguments or request.</param>
        /// <param name="options">The options.</param>
        public static void Record(IServiceProvider services, string operation, string? resource, string? action, IReadOnlyList<object> ids, object? arguments, object? options)
        {
            PipelineLog log = services.GetRequiredService<PipelineLog>();
            log.Add(new PipelineCall(operation, ids, arguments, options, [.. services.GetRequiredService<IConfirmations>().Confirmed]));
            if (!log.SkipWitness && resource is not null && action is not null)
            {
                services.GetRequiredService<AccessWitness>().Record(resource, action);
            }

            // --- A refusing pipeline throws after its evaluation, as the real one does ---
            if (log.Refusal is Exception refusal)
            {
                throw refusal;
            }
        }
    }
}
