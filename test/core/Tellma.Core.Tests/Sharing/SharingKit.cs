// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Queryex;
using Tellma.Core.Reports;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Sharing
{
    /// <summary>
    ///     The stored reports' service over a composition offering English and Arabic, with a scripted
    ///     loader, recording sharing commands and a scripted dashboards' read-through.
    /// </summary>
    internal sealed class SharingKit : IDisposable
    {
        /// <summary>The caller's user id.</summary>
        public const int CallerId = 42;

        /// <summary>The request's time.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The composed services.</summary>
        private readonly ServiceProvider _services;

        /// <summary>Composes the platform with English and Arabic and builds the service.</summary>
        public SharingKit()
        {
            _services = TestComposition.Compose(static tellma => tellma.Languages(["en-GB", "ar-SA"])).BuildServiceProvider(validateScopes: true);
            Metadata = _services.GetRequiredService<IEntityMetadataProvider>();
            StackDescriptor descriptor = _services.GetRequiredService<IStackRegistry>().Find("core.Report")
                ?? throw new InvalidOperationException("The composition has no reports' stack.");
            Service = new ReportsUnderTest(new DescriptorPipelines(descriptor), Commands, Dashboards, _services.GetRequiredService<ReportServiceDependencies>());
        }

        /// <summary>The model's entity metadata.</summary>
        public IEntityMetadataProvider Metadata { get; }

        /// <summary>The composed services.</summary>
        public IServiceProvider Services => _services;

        /// <summary>The sharing commands, which record each call.</summary>
        public RecordingShareCommands Commands { get; } = new();

        /// <summary>The dashboards' read-through, which answers what the test scripts.</summary>
        public ScriptedDashboards Dashboards { get; } = new();

        /// <summary>The loader, which answers what the test scripts.</summary>
        public ScriptedLoader Loader { get; } = new();

        /// <summary>The service under test.</summary>
        public ReportsUnderTest Service { get; }

        /// <summary>The service's hooks, as the pipeline calls them.</summary>
        public IEntityBehavior<Report, int> Hooks => Service;

        /// <summary>The settings of a tenant with English in the first slot and Arabic in the second.</summary>
        /// <returns>The settings.</returns>
        public static TenantSettings Settings()
        {
            return new TenantSettings
            {
                TenantId = 1,
                Tag = new VersionTag(Guid.Empty),
                Names = ["Acme", "أكمي", null],
                Languages = [new LanguageInfo("en", "English", "En", false), new LanguageInfo("ar", "العربية", "ع", true)],
                Shape = MultilingualShape.PrimaryAndSecondary,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = Now,
                Entries = new Dictionary<string, JsonElement>(),
            };
        }

        /// <summary>The context of a request by the caller, or of a system scope.</summary>
        /// <param name="system">Whether the request runs in a system scope.</param>
        /// <returns>The context.</returns>
        public static RequestContext Context(bool system = false)
        {
            return RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en", "ar"], TenantCategory.Live, TenantState.Active, null),
                Kind = system ? PrincipalKind.System : PrincipalKind.User,
                UserId = system ? null : CallerId,
                TenantSettings = Settings(),
                Now = Now,
            };
        }

        /// <summary>A save's context over rows and their before images; a null before image is a new row.</summary>
        /// <param name="system">Whether the save runs in a system scope.</param>
        /// <param name="rows">The rows and their before images.</param>
        /// <returns>The context.</returns>
        public SaveContext<Report> Save(bool system, params (Report Row, Report? Before)[] rows)
        {
            return new SaveContext<Report>
            {
                Entities = [.. rows.Select(static row => row.Row)],
                Options = new SaveOptions(),
                Context = Context(system),
                Loader = Loader,
                Errors = new ValidationErrors<Report>(),
                Host = Refusing.Of<IWriteHost>(),
                NewRows = [.. rows.Select(static row => row.Before is null)],
                BeforeImages = [.. rows.Select(static row => row.Before)],
            };
        }

        /// <summary>A delete's context after its first round.</summary>
        /// <param name="system">Whether the delete runs in a system scope.</param>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets loaded.</param>
        /// <returns>The context.</returns>
        public DeleteContext<Report, int> Delete(bool system, IReadOnlyList<int> ids, params Report[] targets)
        {
            return new DeleteContext<Report, int>
            {
                Ids = ids,
                Context = Context(system),
                Loader = Loader,
                Errors = new ValidationErrors<Report>(),
                Host = Refusing.Of<IWriteHost>(),
                Entities = targets,
            };
        }

        /// <summary>An action's context after its first round.</summary>
        /// <param name="system">Whether the action runs in a system scope.</param>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets loaded.</param>
        /// <returns>The context.</returns>
        public ActionContext<Report, int> Action(bool system, IReadOnlyList<int> ids, params Report[] targets)
        {
            return new ActionContext<Report, int>
            {
                Ids = ids,
                Context = Context(system),
                Loader = Loader,
                Errors = new ValidationErrors<Report>(),
                Batch = Refusing.Of<IDataBatch>(),
                Host = Refusing.Of<IWriteHost>(),
                Entities = targets,
            };
        }

        /// <summary>A report row.</summary>
        /// <param name="id">The id; 0 for a new row.</param>
        /// <param name="owner">The owner; null for a standard row.</param>
        /// <param name="title">The first slot's title.</param>
        /// <returns>The row.</returns>
        public static Report Row(int id = 0, int? owner = CallerId, string title = "Sales")
        {
            return new Report
            {
                Id = id,
                Title = title,
                Root = "core.User",
                Kind = ReportKind.List,
                DefinitionJson = /*lang=json,strict*/ """{ "formatVersion": 1, "list": { "columns": [ { "path": "Name" } ] } }""",
                OwnerId = owner,
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }
    }

    /// <summary>The reports' service, with its record permissions open to a test.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="commands">The sharing commands.</param>
    /// <param name="dashboards">The dashboards' read-through.</param>
    /// <param name="dependencies">The platform's collaborators.</param>
    internal sealed class ReportsUnderTest(IEntityPipelineFactory factory, IShareCommands<Report> commands, IDashboardReadThrough dashboards, ReportServiceDependencies dependencies)
        : ReportService(factory, commands, dashboards, dependencies)
    {
        /// <summary>Why a row refuses an ability, as the permissions extra asks.</summary>
        /// <param name="row">The row.</param>
        /// <param name="ability">The ability.</param>
        /// <returns>The reason, or null.</returns>
        public string? Refuse(Report row, string ability)
        {
            return RefuseOnRecord(row, ability);
        }

        /// <summary>Runs the share action over a context the test built.</summary>
        /// <param name="context">The context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task.</returns>
        public Task RunShareAsync(ActionContext<Report, int> context, ShareArguments arguments)
        {
            return ShareAsync(context, arguments);
        }

        /// <summary>Runs the publish action over a context the test built.</summary>
        /// <param name="context">The context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task.</returns>
        public Task RunPublishAsync(ActionContext<Report, int> context, PublishArguments arguments)
        {
            return PublishAsync(context, arguments);
        }

        /// <summary>Runs the transfer action over a context the test built.</summary>
        /// <param name="context">The context.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A task.</returns>
        public Task RunTransferAsync(ActionContext<Report, int> context, TransferArguments arguments)
        {
            return TransferAsync(context, arguments);
        }
    }

    /// <summary>A pipeline factory whose pipelines answer their stack's descriptor and refuse everything else.</summary>
    /// <param name="descriptor">The descriptor.</param>
    internal sealed class DescriptorPipelines(StackDescriptor descriptor) : IEntityPipelineFactory
    {
        /// <inheritdoc />
        public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            IEntityPipeline<TEntity, TKey> pipeline = DispatchProxy.Create<IEntityPipeline<TEntity, TKey>, DescriptorProxy>();
            ((DescriptorProxy)(object)pipeline).Descriptor = descriptor;
            return pipeline;
        }
    }

    /// <summary>A pipeline stand-in that answers its descriptor alone.</summary>
    public class DescriptorProxy : DispatchProxy
    {
        /// <summary>The descriptor it answers.</summary>
        internal StackDescriptor? Descriptor { get; set; }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == "get_Descriptor"
                ? Descriptor
                : throw new NotSupportedException($"The test reaches {targetMethod?.Name}, which no stand-in answers.");
        }
    }

    /// <summary>Sharing commands that record each call and compose nothing.</summary>
    internal sealed class RecordingShareCommands : IShareCommands<Report>
    {
        /// <summary>The calls, each as its command's name and arguments.</summary>
        public List<(string Command, object Arguments)> Calls { get; } = [];

        /// <inheritdoc />
        public Task ShareAsync(ActionContext<Report, int> context, ShareArguments arguments)
        {
            Calls.Add(("share", arguments));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PublishAsync(ActionContext<Report, int> context, PublishArguments arguments)
        {
            Calls.Add(("publish", arguments));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task TransferAsync(ActionContext<Report, int> context, TransferArguments arguments)
        {
            Calls.Add(("transfer", arguments));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void WithdrawAll(DeletePersistContext<Report> context)
        {
            Calls.Add(("withdraw-all", context));
        }
    }

    /// <summary>A dashboards' read-through that answers the entries the test scripts.</summary>
    internal sealed class ScriptedDashboards : IDashboardReadThrough
    {
        /// <summary>The entries <see cref="DashboardsUsing" /> answers, filtered to the reports asked.</summary>
        public List<(int ReportId, int Id, string? Title)> Entries { get; } = [];

        /// <summary>The reports each call asked about.</summary>
        public List<IReadOnlyList<int>> Asked { get; } = [];

        /// <inheritdoc />
        public Task<FilterTree?> ReadThroughAsync(string dashboardParameter)
        {
            return Task.FromResult<FilterTree?>(null);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<(int ReportId, int Id, string? Title)>> DashboardsUsing(IReadOnlyList<int> reportIds)
        {
            Asked.Add(reportIds);
            return Task.FromResult<IReadOnlyList<(int ReportId, int Id, string? Title)>>([.. Entries.Where(entry => reportIds.Contains(entry.ReportId))]);
        }
    }

    /// <summary>A loader whose visible reads answer the rows the test scripts, and that records each filter.</summary>
    internal sealed class ScriptedLoader : IContextLoader
    {
        /// <summary>The rows a visible read of reports answers, whatever its filter.</summary>
        public List<Report> Visible { get; } = [];

        /// <summary>The filters of the visible reads.</summary>
        public List<FilterTree> VisibleFilters { get; } = [];

        /// <summary>How many times the loads were awaited.</summary>
        public int Loads { get; private set; }

        /// <inheritdoc />
        ContextRef<IReadOnlyList<T>> IContextLoader.Visible<T>(FilterTree filter, QueryArguments? arguments, string? select)
        {
            VisibleFilters.Add(filter);
            return new ContextRef<IReadOnlyList<T>>((IReadOnlyList<T>)(object)Visible.ToList());
        }

        /// <inheritdoc />
        public Task LoadAsync()
        {
            Loads++;
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
        public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> ByParentIds<TChild, TK>(IReadOnlyList<TK> parentIds, string? select = null)
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
        public ContextRef<RawResult> Sql(FormattableString sql, SqlOptions? options = null)
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
}
