// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Calendars;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Tests.Crud.Validation;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Query
{
    /// <summary>
    ///     The query path over the query model, composed whole: the evaluator, the planner and the
    ///     operation are the platform's, while the connection, the guarded runner and the tenant
    ///     database are fakes that hand every batch to a script, under a caller whose grants a test
    ///     sets.
    /// </summary>
    internal sealed class QueryRig : IAsyncDisposable
    {
        /// <summary>The caller's id.</summary>
        public const int UserId = 42;

        /// <summary>The instant every decision is made at.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The composed services.</summary>
        private readonly ServiceProvider _root;

        /// <summary>The next permission's id.</summary>
        private int _nextPermission = 1;

        /// <summary>Composes the model with the fakes.</summary>
        public QueryRig()
        {
            IServiceCollection services = TestComposition.Compose(QueryModel.Compose);
            _ = services.Replace(ServiceDescriptor.Scoped<IScopeConnection>(_ => new Connection(this)));
            _ = services.Replace(ServiceDescriptor.Scoped<IGuardedBatchRunner>(_ => new Runner(this)));
            _ = services.Replace(ServiceDescriptor.Scoped<ITenantDatabase>(provider => new Database(this, provider.GetRequiredService<IQueryexSchemaProvider>())));
            _ = services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
            _root = services.BuildServiceProvider(validateScopes: true);
            User = Connected(UserAccess.System);
        }

        /// <summary>The batches every query hands over, and their scripted answers.</summary>
        public BatchScript Script { get; } = new();

        /// <summary>The clock.</summary>
        public FakeTimeProvider Clock { get; } = new(Now);

        /// <summary>The tenant's language shape, which decides the multilingual twins its schema carries.</summary>
        public MultilingualShape Shape { get; set; } = MultilingualShape.Primary;

        /// <summary>The connected caller every batch is composed for.</summary>
        public ConnectedUser User { get; private set; }

        /// <summary>
        ///     The caller's sign-in assurance: by default the step-up bar's class, reached now, so a
        ///     sensitive read passes; null for a sign-in that never stepped up.
        /// </summary>
        public AuthenticationAssurance? Assurance { get; set; } = new(StepUpBar.DefaultAcr, Now, Now);

        /// <summary>The set a deny re-check finds; null while it finds the caller's own.</summary>
        public ConnectedUser? Revalidated { get; private set; }

        /// <summary>The deny re-checks the scopes' connections ran.</summary>
        public int Revalidations { get; private set; }

        /// <summary>What connecting the caller throws, as a connect that failed would; null while it connects.</summary>
        public Exception? ConnectFailure { get; set; }

        /// <summary>What a re-check throws, as a revalidation that failed would; null while it finds a set.</summary>
        public Exception? RevalidateFailure { get; set; }

        /// <summary>The root services.</summary>
        public IServiceProvider Services => _root;

        /// <summary>The tenant's schema in the current shape.</summary>
        public QueryexSchema Schema => _root.GetRequiredService<IQueryexSchemaProvider>().GetSchema(Shape);

        /// <summary>The surfaces.</summary>
        public QuerySurfaces Surfaces => _root.GetRequiredService<QuerySurfaces>();

        /// <summary>A page of the given ids, the answer a row read gets.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The page.</returns>
        public static RowPage Page(params int[] ids)
        {
            return new RowPage(ValidationRig.IntRows(["Id"], [.. ids.Select(static id => new int?[] { id })]), null, null, null);
        }

        /// <summary>Connects the caller with grants, each a resource, an action and an optional filter.</summary>
        /// <param name="grants">The grants.</param>
        public void Grant(params (string Resource, string Action, string? Filter)[] grants)
        {
            User = Connect(grants);
        }

        /// <summary>Makes the next deny re-check find the caller with grants, as a grant saved since would.</summary>
        /// <param name="grants">The grants the re-check finds.</param>
        public void GrantOnRecheck(params (string Resource, string Action, string? Filter)[] grants)
        {
            Revalidated = Connect(grants);
        }

        /// <summary>A scope of the caller: user 42 of tenant 1.</summary>
        /// <returns>The scope.</returns>
        public AsyncServiceScope Scope()
        {
            AsyncServiceScope scope = _root.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IRequestContextHolder>().Set(Context());
            return scope;
        }

        /// <summary>Lowers a search on a surface in the current shape, rendered.</summary>
        /// <param name="resource">The stack's or the child surface's resource.</param>
        /// <param name="text">The text.</param>
        /// <param name="custom">The service's branch, or null.</param>
        /// <returns>The lowered search.</returns>
        public SearchFilter Search(string resource, string text, FilterTree? custom = null)
        {
            QuerySurface surface = Surfaces.Stack(resource) ?? Surfaces.Child(resource) ?? throw new ArgumentException($"No surface is named '{resource}'.", nameof(resource));
            return QuerySearch.Lower(surface, Schema.FindEntity(resource)!, text, custom);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _root.DisposeAsync();
        }

        /// <summary>
        ///     Renders a filter tree in a compact text form: <c>and(…)</c>, <c>or(…)</c>, <c>not(…)</c>,
        ///     <c>via Navigation(…)</c>, <c>exists Entity.Navigation(…)</c>, leaves as written.
        /// </summary>
        /// <param name="tree">The tree; null renders as <c>-</c>.</param>
        /// <returns>The text.</returns>
        public static string Render(FilterTree? tree)
        {
            StringBuilder text = new();
            Write(text, tree);
            return text.ToString();

            static void Write(StringBuilder text, FilterTree? tree)
            {
                switch (tree)
                {
                    case null:
                        _ = text.Append('-');
                        break;
                    case FilterTree.LeafNode leaf:
                        _ = text.Append(leaf.Text);
                        break;
                    case FilterTree.AndNode and:
                        List(text, "and", and.Children);
                        break;
                    case FilterTree.OrNode or:
                        List(text, "or", or.Children);
                        break;
                    case FilterTree.NotNode not:
                        List(text, "not", [not.Operand]);
                        break;
                    case FilterTree.ViaNode via:
                        List(text, "via " + via.Navigation, [via.Inner]);
                        break;
                    case FilterTree.ExistsNode exists:
                        List(text, "exists " + exists.Entity + "." + exists.Navigation, [exists.Inner]);
                        break;
                    default:
                        _ = text.Append(tree.GetType().Name);
                        break;
                }
            }

            static void List(StringBuilder text, string name, IReadOnlyList<FilterTree> children)
            {
                _ = text.Append(name).Append('(');
                for (int index = 0; index < children.Count; index++)
                {
                    if (index > 0)
                    {
                        _ = text.Append(" | ");
                    }

                    Write(text, children[index]);
                }

                _ = text.Append(')');
            }
        }

        /// <summary>The caller connected now with grants, each a resource, an action and an optional filter.</summary>
        /// <param name="grants">The grants.</param>
        /// <returns>The connected caller.</returns>
        private ConnectedUser Connect((string Resource, string Action, string? Filter)[] grants)
        {
            PermissionRow[] rows =
            [
                .. grants.Select(grant => new PermissionRow(
                    _nextPermission++,
                    10,
                    "Queriers",
                    false,
                    grant.Resource,
                    grant.Action,
                    grant.Filter,
                    grant.Filter is null ? null : QueryexLanguage.Version,
                    null,
                    null)),
            ];
            UserAccess access = _root.GetRequiredService<UserAccessBuilder>().Build(1, UserId, Guid.Empty, Guid.Empty, rows, Schema);
            return Connected(access);
        }

        /// <summary>A connected caller with a set.</summary>
        /// <param name="access">The set.</param>
        /// <returns>The connected user.</returns>
        private static ConnectedUser Connected(UserAccess access)
        {
            return new ConnectedUser(
                UserId,
                UserKind.Individual,
                UserState.Joined,
                new UserProfile("Ada", null, null, null, null, null, null, null, null, null, null, null, null, null, Now),
                Guid.Empty,
                access,
                new Dictionary<string, Guid>(StringComparer.Ordinal),
                null);
        }

        /// <summary>The caller's context: user 42 of tenant 1, with its settings in the rig's shape.</summary>
        /// <returns>The context.</returns>
        private RequestContext Context()
        {
            return RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.User,
                Subject = "sub-42",
                UserId = UserId,
                Assurance = Assurance,
                TenantSettings = new TenantSettings
                {
                    TenantId = 1,
                    Tag = new VersionTag(Guid.Empty),
                    Names = ["Acme", null, null],
                    Languages = [new LanguageInfo("en", "English", "E", false)],
                    Shape = Shape,
                    Calendars = [],
                    TimeZone = TimeZoneInfo.Utc,
                    SqlServerTimeZoneName = "UTC",
                    ModifiedAt = Now,
                    Entries = new Dictionary<string, JsonElement>(),
                },
            };
        }

        /// <summary>
        ///     The scope's connection: the rig's caller, cached; a re-check finds the set the rig
        ///     names, else the same; either fails with what the rig names.
        /// </summary>
        /// <param name="rig">The rig.</param>
        private sealed class Connection(QueryRig rig) : IScopeConnection
        {
            /// <inheritdoc />
            public RequestContextInputs Inputs { get; } = new(null, null, null, null, null, null, SuppressActivityStamp: true);

            /// <inheritdoc />
            public Task<ConnectedUser> CurrentAsync(CancellationToken cancellationToken)
            {
                return rig.ConnectFailure is Exception failure ? Task.FromException<ConnectedUser>(failure) : Task.FromResult(rig.User);
            }

            /// <inheritdoc />
            public Task<ConnectedUser> RevalidateAsync(ConnectedUser user, CancellationToken cancellationToken)
            {
                // The re-check applies what it found to the scope's connection, as a real one does.
                rig.Revalidations++;
                if (rig.RevalidateFailure is Exception failure)
                {
                    return Task.FromException<ConnectedUser>(failure);
                }

                rig.User = rig.Revalidated ?? rig.User;
                return Task.FromResult(rig.User);
            }

            /// <inheritdoc />
            public ConnectPremises PremisesFor(ConnectedUser user)
            {
                throw new NotSupportedException("The rig's runner composes no prologue.");
            }

            /// <inheritdoc />
            public BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises)
            {
                throw new NotSupportedException("The rig's runner composes no prologue.");
            }

            /// <inheritdoc />
            public ConnectResult? ReadOf(BatchResult<ConnectResult> prologue)
            {
                throw new NotSupportedException("The rig's runner composes no prologue.");
            }

            /// <inheritdoc />
            public ConnectedUser Apply(ConnectResult result, ConnectedUser basis)
            {
                throw new NotSupportedException("The rig's runner composes no prologue.");
            }

            /// <inheritdoc />
            public Task<ConnectedUser> ReconnectColdAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The rig's runner never reconnects.");
            }

            /// <inheritdoc />
            public Task<UserAccess?> LoadDetachedAsync(int userId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The query path loads no other user.");
            }

            /// <inheritdoc />
            public void Drop(ConnectedUser user)
            {
            }
        }

        /// <summary>A guarded runner that composes one scripted batch for the rig's caller and executes it.</summary>
        /// <param name="rig">The rig.</param>
        private sealed class Runner(QueryRig rig) : IGuardedBatchRunner
        {
            /// <inheritdoc />
            public async Task<TResult> RunAsync<TResult>(
                BatchPurpose purpose,
                Func<ConnectedUser, IDataBatch, ValueTask> compose,
                Func<ConnectedUser, BatchOutcome, TResult> read,
                CancellationToken cancellationToken = default)
            {
                ScriptedBatch batch = rig.Script.NewBatch(purpose);
                await compose(rig.User, batch);
                BatchOutcome outcome = await batch.ExecuteAsync(cancellationToken);
                return read(rig.User, outcome);
            }
        }

        /// <summary>The tenant's database: the schema in the rig's shape, the caller's context values, and no batch of its own.</summary>
        /// <param name="rig">The rig.</param>
        /// <param name="schemas">The schema provider.</param>
        private sealed class Database(QueryRig rig, IQueryexSchemaProvider schemas) : ITenantDatabase
        {
            /// <inheritdoc />
            public QueryexSchema Schema => schemas.GetSchema(rig.Shape);

            /// <inheritdoc />
            public QueryexContextValues Context { get; } = new(new DateOnly(2026, 11, 17), Now, UserId, "UTC");

            /// <inheritdoc />
            public IDataBatch CreateBatch(BatchPurpose purpose)
            {
                throw new NotSupportedException("The rig's batches come from its script.");
            }
        }
    }
}
