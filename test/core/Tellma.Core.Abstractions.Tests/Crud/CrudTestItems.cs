// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Tests.Jobs;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Crud
{
    /// <summary>The rows, contexts and collaborators the CRUD contract tests build their subjects from.</summary>
    internal static class CrudTestItems
    {
        /// <summary>The instant every test context is set at.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

        /// <summary>A tenantless request context on a calendar that is never asked anything.</summary>
        /// <returns>The context.</returns>
        public static RequestContext Context()
        {
            return RequestContext.Tenantless(RecordingProxy.For<ICalendarSystem>().Proxy, Now);
        }

        /// <summary>A context loader whose every member throws: the contracts hold one without using it.</summary>
        /// <returns>The loader.</returns>
        public static IContextLoader UnusedLoader()
        {
            return DispatchProxy.Create<IContextLoader, RefusingBatch>();
        }

        /// <summary>A batch whose every member throws.</summary>
        /// <returns>The batch.</returns>
        public static IDataBatch UnusedBatch()
        {
            return JobTestItems.UnusedBatch();
        }

        /// <summary>A write host that is not a platform frame, named <c>test.operation</c>.</summary>
        /// <returns>The host.</returns>
        public static IWriteHost ForeignHost()
        {
            (IWriteHost host, RecordingProxy recorder) = RecordingProxy.For<IWriteHost>();
            recorder.Answer = static (method, _) => method.Name == "get_Operation" ? "test.operation" : null;
            return host;
        }

        /// <summary>A save context over the given rows, each flagged new or not.</summary>
        /// <param name="rows">The payload.</param>
        /// <param name="isNew">Whether each row is an insert; null for a context no pipeline prepared.</param>
        /// <returns>The context.</returns>
        public static SaveContext<Widget> SaveContext(IReadOnlyList<Widget> rows, IReadOnlyList<bool>? isNew)
        {
            return new SaveContext<Widget>
            {
                Entities = rows,
                Options = new SaveOptions(),
                Context = Context(),
                Loader = UnusedLoader(),
                Errors = new ValidationErrors<Widget>(),
                Host = ForeignHost(),
                NewRows = isNew,
            };
        }

        /// <summary>A save-persist context over the given rows.</summary>
        /// <param name="rows">The payload.</param>
        /// <returns>The context.</returns>
        public static SavePersistContext<Widget> SavePersistContext(IReadOnlyList<Widget> rows)
        {
            return new SavePersistContext<Widget>
            {
                Entities = rows,
                Context = Context(),
                Batch = UnusedBatch(),
                AffectedIds = new SqlIdentifier("@affected"),
                Captures = [],
                Options = new SaveOptions(),
                NewIds = new SqlIdentifier("@new"),
                TouchedIds = new SqlIdentifier("@touched"),
            };
        }
    }

    /// <summary>A stack's entity: an activatable row with one scalar.</summary>
    public sealed class Widget : Entity<int>, IActivatable
    {
        /// <summary>A scalar.</summary>
        public string? Name { get; set; }

        /// <inheritdoc />
        public bool IsActive { get; set; }
    }

    /// <summary>A tree stack's entity.</summary>
    public sealed class Node : TreeEntity<int>;

    /// <summary>A proxy that records every call and answers from a callback, else with an empty value.</summary>
    public class RecordingProxy : DispatchProxy
    {
        /// <summary>The calls so far.</summary>
        private readonly List<RecordedCall> _calls = [];

        /// <summary>The calls so far, in order.</summary>
        public IReadOnlyList<RecordedCall> Calls => _calls;

        /// <summary>Answers a call; a null answer falls back to the return type's empty value.</summary>
        public Func<MethodInfo, object?[], object?>? Answer { get; set; }

        /// <summary>Creates a recording proxy of an interface.</summary>
        /// <typeparam name="T">The interface.</typeparam>
        /// <returns>The proxy, and its recorder.</returns>
        public static (T Proxy, RecordingProxy Recorder) For<T>()
            where T : class
        {
            T proxy = Create<T, RecordingProxy>();
            return (proxy, (RecordingProxy)(object)proxy);
        }

        /// <summary>The one call recorded, which must have been made to the named member.</summary>
        /// <param name="name">The member's name.</param>
        /// <returns>The call.</returns>
        public RecordedCall Single(string name)
        {
            RecordedCall call = Assert.Single(_calls);
            Assert.Equal(name, call.Method.Name);
            return call;
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            object?[] arguments = args ?? [];
            _calls.Add(new RecordedCall(targetMethod, arguments));
            return Answer?.Invoke(targetMethod, arguments) ?? Empty(targetMethod.ReturnType);
        }

        /// <summary>The empty value of a return type: a completed task, a default value, or null.</summary>
        /// <param name="type">The return type.</param>
        /// <returns>The value.</returns>
        private static object? Empty(Type type)
        {
            if (type == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                // Task.FromResult over the result type's own empty value.
                Type result = type.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result).Invoke(null, [Empty(result)]);
            }

            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }

    /// <summary>One recorded call.</summary>
    /// <param name="Method">The member called.</param>
    /// <param name="Arguments">Its arguments.</param>
    public sealed record RecordedCall(MethodInfo Method, IReadOnlyList<object?> Arguments);
}
