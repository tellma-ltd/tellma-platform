// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Data;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Calendars;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     What the access rules' tests share: a registry of widgets, kinds, a resource whose
    ///     securables take two roots and a report, over a schema of widgets and kinds; a clock; the
    ///     access instruments and logs; and the contexts the rules validate.
    /// </summary>
    internal sealed class AccessRulesHarness : IDisposable
    {
        /// <summary>The widgets' resource, whose securables filter over widgets.</summary>
        public const string Widget = "fx.Widget";

        /// <summary>The kinds' resource.</summary>
        public const string Kind = "fx.Kind";

        /// <summary>A resource whose two securables name two roots, so no filter binds on it.</summary>
        public const string Mixed = "fx.Mixed";

        /// <summary>A report, whose one securable takes no filter.</summary>
        public const string Report = "fx.Report";

        /// <summary>The instant every test runs at.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The fixture's securables; the widgets' former name is an alias.</summary>
        public static readonly SecurableRegistry Registry = SecurableRegistryTests.Compose(("fixture", static builder => builder
            .Add(Widget, AccessActions.Read, Widget)
            .Add(Widget, AccessActions.Save, Widget)
            .Add(Widget, "Approve", null)
            .Add(Kind, AccessActions.Read, Kind)
            .Add(Mixed, AccessActions.Read, Widget)
            .Add(Mixed, AccessActions.Save, Kind)
            .Add(Report, "Run", null)
            .Alias("fx.Gadget", Widget)));

        /// <summary>The fixture's schema: widgets with an amount and a kind.</summary>
        public static readonly QueryexSchema Schema = BuildSchema();

        /// <summary>The meter factory and the logs.</summary>
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Trace))
            .AddFakeLogging()
            .BuildServiceProvider();

        /// <summary>Creates the harness, its instruments over its own meter factory.</summary>
        public AccessRulesHarness()
        {
            Metrics = new AccessMetrics(_services.GetRequiredService<IMeterFactory>());
            Guards = new MetricCollector<long>(_services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.GuardsTriggered);
        }

        /// <summary>The host's clock, at <see cref="Now" />.</summary>
        public FakeTimeProvider Time { get; } = new(Now);

        /// <summary>The access instruments.</summary>
        public AccessMetrics Metrics { get; }

        /// <summary>The guards counter's measurements.</summary>
        public MetricCollector<long> Guards { get; }

        /// <summary>The logger factory, whose records the fake collector keeps.</summary>
        public ILoggerFactory LoggerFactory => _services.GetRequiredService<ILoggerFactory>();

        /// <summary>The access options, the defaults unless a test changes them.</summary>
        public AccessOptions Options { get; } = new();

        /// <summary>The guard tag of each guards measurement, in order.</summary>
        public IReadOnlyList<string?> GuardTags => [.. Guards.GetMeasurementSnapshot().Select(static measurement => measurement.Tags[AccessTelemetryNames.GuardTag] as string)];

        /// <summary>A caller holding unrestricted, permanent grants on pairs.</summary>
        /// <param name="userId">The caller.</param>
        /// <param name="pairs">The pairs.</param>
        /// <returns>The caller's set.</returns>
        public static UserAccess Caller(int userId, params (string Resource, string Action)[] pairs)
        {
            return Caller(userId, [.. pairs.Select(static pair => Grant(pair.Resource, pair.Action))]);
        }

        /// <summary>A caller holding grants.</summary>
        /// <param name="userId">The caller.</param>
        /// <param name="grants">The grants.</param>
        /// <returns>The caller's set.</returns>
        public static UserAccess Caller(int userId, IReadOnlyList<RoleAccessGrant> grants)
        {
            return new UserAccess
            {
                TenantId = 1,
                UserId = userId,
                ComputedAt = Now,
                Grants = grants,
                ActionResources = ActionResourceMap.From(Registry.All),
            };
        }

        /// <summary>A grant of a private role.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="filter">The filter; null for none.</param>
        /// <param name="startsAt">The membership's start; null since always.</param>
        /// <param name="endsAt">The membership's end; null for none.</param>
        /// <returns>The grant.</returns>
        public static RoleAccessGrant Grant(string resource, string action, string? filter = null, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null)
        {
            return new RoleAccessGrant(resource, action, 1, 2, "Role", false, filter, filter is null ? null : QueryexLanguage.Version, startsAt, endsAt);
        }

        /// <summary>The request context of a user scope of tenant 1.</summary>
        /// <param name="userId">The caller; null for none.</param>
        /// <returns>The context.</returns>
        public static RequestContext Context(int? userId)
        {
            return RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.User,
                UserId = userId,
            };
        }

        /// <summary>A save's context after its first round: the payload, which rows are new, and the before images.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <param name="loader">The loader.</param>
        /// <param name="callerId">The caller.</param>
        /// <param name="rows">The payload, each with its before image; null for a new row.</param>
        /// <returns>The context.</returns>
        public static SaveContext<TEntity> Save<TEntity>(IContextLoader loader, int? callerId, params (TEntity Row, TEntity? Before)[] rows)
            where TEntity : class
        {
            return new SaveContext<TEntity>
            {
                Entities = [.. rows.Select(static row => row.Row)],
                Options = new SaveOptions(),
                Context = Context(callerId),
                Loader = loader,
                Errors = new ValidationErrors<TEntity>(),
                Host = Refusing.Of<IWriteHost>(),
                NewRows = [.. rows.Select(static row => row.Before is null)],
                BeforeImages = [.. rows.Select(static row => row.Before)],
            };
        }

        /// <summary>A delete's context after its first round: the ids asked and the targets loaded.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <param name="loader">The loader.</param>
        /// <param name="callerId">The caller.</param>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets loaded.</param>
        /// <returns>The context.</returns>
        public static DeleteContext<TEntity, int> Delete<TEntity>(IContextLoader loader, int? callerId, IReadOnlyList<int> ids, params TEntity[] targets)
            where TEntity : class
        {
            return new DeleteContext<TEntity, int>
            {
                Ids = ids,
                Context = Context(callerId),
                Loader = loader,
                Errors = new ValidationErrors<TEntity>(),
                Host = Refusing.Of<IWriteHost>(),
                Entities = targets,
            };
        }

        /// <summary>An action's context after its first round: the ids asked and the targets loaded.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <param name="loader">The loader.</param>
        /// <param name="callerId">The caller.</param>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets loaded.</param>
        /// <returns>The context.</returns>
        public static ActionContext<TEntity, int> Action<TEntity>(IContextLoader loader, int? callerId, IReadOnlyList<int> ids, params TEntity[] targets)
            where TEntity : class
        {
            return new ActionContext<TEntity, int>
            {
                Ids = ids,
                Context = Context(callerId),
                Loader = loader,
                Errors = new ValidationErrors<TEntity>(),
                Batch = Refusing.Of<IDataBatch>(),
                Host = Refusing.Of<IWriteHost>(),
                Entities = targets,
            };
        }

        /// <summary>The errors of a context, each as its path and code.</summary>
        /// <param name="errors">The errors.</param>
        /// <returns>The pairs, in order.</returns>
        public static IReadOnlyList<(string Path, string Code)> Errors(ValidationErrors errors)
        {
            return [.. errors.Items.Select(static error => (error.Path.ToString(), error.Code))];
        }

        /// <summary>The access log's records of one event.</summary>
        /// <param name="eventId">The event.</param>
        /// <returns>The records, in order.</returns>
        public IReadOnlyList<FakeLogRecord> Logs(int eventId)
        {
            return [.. _services.GetFakeLogCollector().GetSnapshot().Where(record => record.Id.Id == eventId)];
        }

        /// <summary>The options, as the rules take them.</summary>
        /// <returns>The options.</returns>
        public IOptions<AccessOptions> AccessOptions()
        {
            return Microsoft.Extensions.Options.Options.Create(Options);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Guards.Dispose();
            _services.Dispose();
        }

        /// <summary>Builds the fixture's schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema BuildSchema()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity(Kind, "[fx].[Kinds]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true);
            _ = builder.Entity(Widget, "[fx].[Widgets]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true)
                .Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true)
                .Property("KindId", QueryexType.QxNumeric, "KindId", isNotNull: true)
                .Navigation("Kind", Kind, "KindId");
            return builder.Build();
        }
    }

    /// <summary>An evaluator that answers the caller's set and nothing else.</summary>
    /// <param name="access">The caller's set.</param>
    internal sealed class FixedEvaluator(UserAccess access) : IAccessEvaluator
    {
        /// <inheritdoc />
        public Task<UserAccess> GetAccessAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(access);
        }

        /// <inheritdoc />
        public Task<AccessDecision> EvaluateAsync(string resource, string action, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<AccessDecision> RequireAsync(string resource, string action, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AccessDecision>> EvaluateAsync(IReadOnlyList<SecurableRef> securables, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<AccessDecision> EvaluateForAsync(int userId, string resource, string action, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AccessDecision>> EvaluateAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public void Invalidate(int tenantId, int userId)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>A request context accessor that answers one context.</summary>
    /// <param name="context">The context.</param>
    internal sealed class FixedContextAccessor(RequestContext context) : IRequestContextAccessor
    {
        /// <inheritdoc />
        public RequestContext Current => context;
    }

    /// <summary>A tenant database that answers its schema and nothing else.</summary>
    /// <param name="schema">The schema.</param>
    internal sealed class SchemaDatabase(QueryexSchema schema) : ITenantDatabase
    {
        /// <inheritdoc />
        public QueryexSchema Schema => schema;

        /// <inheritdoc />
        public QueryexContextValues Context => throw new NotSupportedException();

        /// <inheritdoc />
        public IDataBatch CreateBatch(BatchPurpose purpose)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    ///     A loader that answers the access rules' loads from what the test gives it: the permissions
    ///     of each role, and the roles that have members. It records what it was asked.
    /// </summary>
    internal sealed class RulesLoader : IContextLoader
    {
        /// <summary>The permissions of each role.</summary>
        public Dictionary<int, IReadOnlyList<Permission>> Permissions { get; } = [];

        /// <summary>The roles that have members.</summary>
        public HashSet<int> WithMembers { get; } = [];

        /// <summary>The roles each permissions load asked for, in order.</summary>
        public List<IReadOnlyList<int>> PermissionLoads { get; } = [];

        /// <summary>The number of raw loads asked.</summary>
        public int SqlLoads { get; private set; }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> ByParentIds<TChild, TK>(IReadOnlyList<TK> parentIds, string? select = null)
            where TK : notnull
        {
            // The rules ask for the permissions of roles, keyed by the role.
            var ids = (IReadOnlyList<int>)parentIds;
            PermissionLoads.Add(ids);
            var found = ids.Where(Permissions.ContainsKey).ToDictionary(static id => id, id => Permissions[id]);
            return new ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>>((IReadOnlyDictionary<TK, IReadOnlyList<TChild>>)(object)found);
        }

        /// <inheritdoc />
        public ContextRef<RawResult> Sql(FormattableString sql, SqlOptions? options = null)
        {
            // The one raw load is of the roles with members among a list of ids, its one table hole.
            SqlLoads++;
            IReadOnlyList<int> asked = [.. sql.GetArguments().OfType<IEnumerable<IdList>>().Single().Select(static key => key.Id)];
            using DataTable table = new();
            _ = table.Columns.Add("RoleId", typeof(int));
            foreach (int id in asked.Where(WithMembers.Contains))
            {
                _ = table.Rows.Add(id);
            }

            QueryRowSet.Builder builder = new([new QueryColumn("RoleId", QueryexType.QxNumeric, null, null, false, null, false)], [typeof(int)], table.Rows.Count);
            using DataTableReader reader = table.CreateDataReader();
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return new ContextRef<RawResult>(new RawResult([builder.Build()]));
        }

        /// <inheritdoc />
        public Task LoadAsync()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, T>> ByIds<T, TK>(IReadOnlyList<TK> ids, string? select = null)
            where TK : notnull
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, T>> ByKey<T, TK>(string key, IReadOnlyList<TK> values, string? select = null)
            where TK : notnull
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyList<T>> Query<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyList<T>> Visible<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ContextRef<QueryRowSet> Rows(QuerySpec spec, QueryArguments? arguments = null)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ContextRef<bool> Exists(string root, FilterTree filter, QueryArguments? arguments = null)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ContextRef<int> Count(string root, FilterTree filter, int cap, QueryArguments? arguments = null)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Ancestors<T, TK>(IReadOnlyList<TK> ids)
            where TK : notnull
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public void Prime<T, TK>(T entity)
            where TK : notnull
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>A pipeline factory whose pipelines refuse every operation, for a service no test call reaches.</summary>
    internal sealed class RefusingPipelines : IEntityPipelineFactory
    {
        /// <inheritdoc />
        public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            return Refusing.Of<IEntityPipeline<TEntity, TKey>>();
        }
    }

    /// <summary>A stand-in for an interface no test call reaches: every member refuses.</summary>
    public class Refusing : DispatchProxy
    {
        /// <summary>A stand-in for an interface.</summary>
        /// <typeparam name="T">The interface.</typeparam>
        /// <returns>The stand-in.</returns>
        public static T Of<T>()
            where T : class
        {
            return Create<T, Refusing>();
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new NotSupportedException($"The test reaches {targetMethod?.Name}, which no stand-in answers.");
        }
    }
}
