// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;
using Tellma.Core.Queryex;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The caller's self-service in memory: a locale change's assignments and its refusals at the
    ///     request's members, a profile edit's mapping onto the stored row, its stamp check before
    ///     anything is enlisted, its paths without the row index, and the envelope's tags and access
    ///     summary.
    /// </summary>
    public sealed class UserSelfServiceTests : IDisposable
    {
        /// <summary>The stamp the caller's row carries as stored.</summary>
        private static readonly DateTimeOffset Stored = new(2026, 11, 19, 8, 30, 0, TimeSpan.Zero);

        /// <summary>The runner that answers a profile edit's first round trip.</summary>
        private readonly SelfServiceRunner _runner = new();

        /// <summary>The open frame a profile edit enlists into.</summary>
        private readonly RecordingOpenHost _host = new();

        /// <summary>The service's collaborators, over the runner and the frame.</summary>
        private readonly UsersKit _kit;

        /// <summary>Composes the kit over the test's runner and frame.</summary>
        public UserSelfServiceTests()
        {
            _kit = new UsersKit(_runner, _host);
            _kit.EnterContext();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        // --- The locale change ---

        [Fact]
        public void A_locale_change_assigns_each_member_set_as_the_deployment_spells_it()
        {
            IReadOnlyDictionary<string, object?> assignments = Assign(new MeLocaleRequest(" AR ", "UQ", "asia/riyadh", " sa ", DigitStyle.Native));

            Assert.Equal(
                [
                    (nameof(User.PreferredCalendar), (object?)"uq"),
                    (nameof(User.PreferredDigits), DigitStyle.Native),
                    (nameof(User.PreferredLanguage), "ar"),
                    (nameof(User.PreferredRegion), "SA"),
                    (nameof(User.PreferredTimeZone), "Asia/Riyadh"),
                ],
                Sorted(assignments));
        }

        [Fact]
        public void A_locale_change_assigns_one_member_alone_and_leaves_the_rest()
        {
            Assert.Equal([(nameof(User.PreferredTimeZone), (object?)"Asia/Dubai")], Sorted(Assign(new MeLocaleRequest(null, null, "Asia/Dubai", null, null))));
            Assert.Equal([(nameof(User.PreferredDigits), (object?)DigitStyle.Latin)], Sorted(Assign(new MeLocaleRequest(null, null, null, null, DigitStyle.Latin))));
        }

        [Fact]
        public void A_reset_member_is_assigned_null_beside_the_members_set()
        {
            IReadOnlyDictionary<string, object?> assignments = Assign(new MeLocaleRequest("en", null, null, null, null, [MeLocaleMember.Region, MeLocaleMember.Digits, MeLocaleMember.Region]));

            Assert.Equal(
                [(nameof(User.PreferredDigits), null), (nameof(User.PreferredLanguage), "en"), (nameof(User.PreferredRegion), null)],
                Sorted(assignments));
        }

        [Fact]
        public void A_locale_change_that_sets_and_resets_nothing_is_refused_at_the_language()
        {
            Assert.Equal([("Language", AccessCodes.LocaleEmpty)], Refusals(new MeLocaleRequest(null, null, null, null, null)));
            Assert.Equal([("Language", AccessCodes.LocaleEmpty)], Refusals(new MeLocaleRequest("  ", "", null, " ", null, [])));
            Assert.Equal([("Language", AccessCodes.LocaleEmpty)], Refusals(new MeLocaleRequest(null, null, "", null, null)));
        }

        [Fact]
        public void An_empty_zone_beside_a_region_is_not_set_and_only_the_region_is_assigned()
        {
            Assert.Equal([(nameof(User.PreferredRegion), (object?)"SA")], Sorted(Assign(new MeLocaleRequest(null, null, "", "sa", null))));
        }

        [Fact]
        public void A_member_both_set_and_reset_is_refused_at_the_reset()
        {
            Assert.Equal([("Reset", AccessCodes.LocaleResetConflict)], Refusals(new MeLocaleRequest("en", null, null, null, DigitStyle.Latin, [MeLocaleMember.Digits])));
        }

        [Theory]
        [InlineData("ar-SA")]
        [InlineData("fr")]
        public void A_language_not_offered_or_regioned_is_refused_at_the_language(string language)
        {
            Assert.Equal([("Language", AccessCodes.LanguageNotOffered)], Refusals(new MeLocaleRequest(language, null, null, null, null)));
        }

        [Theory]
        [InlineData("Asia/Calcutta")]
        [InlineData("Mars/Olympus")]
        public void A_zone_outside_the_set_an_alias_included_is_refused_at_the_time_zone(string zone)
        {
            Assert.Equal([("TimeZone", AccessCodes.TimeZoneUnknown)], Refusals(new MeLocaleRequest(null, null, zone, null, null)));
        }

        [Fact]
        public void Each_value_not_offered_is_refused_at_its_member_all_at_once()
        {
            Assert.Equal(
                [("Calendar", AccessCodes.CalendarUnknown), ("Region", AccessCodes.RegionNotOffered)],
                Refusals(new MeLocaleRequest(null, "xx", null, "ZZ", null)));
        }

        // --- The profile edit ---

        [Fact]
        public void A_profile_edit_copies_the_eight_members_and_leaves_every_other_member_as_stored()
        {
            User row = StoredRow();
            MeSaveRequest request = new("Ada L.", "آدا", "Ada3", 7, 8, Gender.Female, "ada@contact.example", "+971501234567", Stored);

            UserService<User>.Apply(row, request);

            Assert.Equal(("Ada L.", "آدا", "Ada3", 7, 8, (Gender?)Gender.Female, "ada@contact.example", "+971501234567"), (row.Name, row.Name2, row.Name3, row.ImageId, row.SignatureId, row.Gender, row.ContactEmail, row.ContactMobile));
            Assert.Null(row.RoleMemberships);
            Assert.Equal(("ada@example.com", "ar", "uq", "Asia/Riyadh", "SA", (DigitStyle?)DigitStyle.Native, Stored), (row.Email, row.PreferredLanguage, row.PreferredCalendar, row.PreferredTimeZone, row.PreferredRegion, row.PreferredDigits, row.ModifiedAt));
        }

        [Fact]
        public void The_enlisted_save_s_paths_drop_the_row_index()
        {
            Assert.Equal("ContactEmail", UserService<User>.WithoutRowIndex(ValidationPath.Root.Index(0).Property("ContactEmail")).ToString());
            Assert.Equal("ImageId", UserService<User>.WithoutRowIndex(ValidationPath.Root.Index(0).Property("ImageId")).ToString());
            Assert.Equal("Name", UserService<User>.WithoutRowIndex(ValidationPath.Root.Property("Name")).ToString());
        }

        [Fact]
        public async Task A_stale_stamp_conflicts_before_anything_is_enlisted_naming_the_modifier_in_the_reader_s_language()
        {
            _runner.Row = StoredRow();
            _runner.Modifier = ("Ann", null, null);

            ConcurrencyException refused = await Assert.ThrowsAsync<ConcurrencyException>(() => _kit.Service.RunSaveMeAsync(Request(Stored.AddMinutes(-1)), TestContext.Current.CancellationToken));

            ConcurrencyConflict conflict = Assert.Single(refused.Conflicts);
            Assert.Equal((UsersKit.CallerId, Stored.ToString(PersistFailures.StampFormat, CultureInfo.InvariantCulture), 5, "Ann", false), (conflict.Id, conflict.ModifiedAt, conflict.ModifiedById, conflict.ModifiedByName, conflict.IsMissing));
            Assert.Empty(_host.Enlisted);
            Assert.Equal(0, _host.Persists);
            Assert.Equal([BatchPurpose.Read], _runner.Purposes);
        }

        [Fact]
        public void A_conflict_names_the_modifier_in_the_reader_s_content_language_falling_back_to_the_primary_name()
        {
            User row = StoredRow();
            Assert.Equal("آن", UserService<User>.Conflict(row, Names("Ann", "آن", null), 2).Conflicts[0].ModifiedByName);
            Assert.Equal("Ann", UserService<User>.Conflict(row, Names("Ann", "آن", null), 3).Conflicts[0].ModifiedByName);
            Assert.Null(UserService<User>.Conflict(row, Names(), 1).Conflicts[0].ModifiedByName);
        }

        [Fact]
        public async Task An_override_enlists_the_edit_despite_a_stale_stamp_and_saves_under_override()
        {
            _runner.Row = StoredRow();

            MeResult result = await _kit.Service.RunSaveMeAsync(Request(Stored.AddMinutes(-1), ConcurrencyMode.Override), TestContext.Current.CancellationToken);

            (User row, EnlistSaveOptions options) = Assert.Single(_host.Enlisted);
            Assert.Equal(ConcurrencyMode.Override, options.Concurrency);
            Assert.Equal("Ada L.", row.Name);
            Assert.Equal(1, _host.Persists);
            Assert.Equal("Ada L.", result.User.Name);
        }

        [Fact]
        public async Task A_current_stamp_enlists_the_stored_row_under_check_and_answers_the_saved_profile_the_bag_and_the_persist_s_tags()
        {
            _runner.Row = StoredRow();
            _runner.Bag = [("nav.pinned", "[1]"), ("theme", "dark")];

            MeResult result = await _kit.Service.RunSaveMeAsync(Request(Stored), TestContext.Current.CancellationToken);

            // --- One enlistment of the caller's stored row, the memberships left out, under check ---
            (User row, EnlistSaveOptions options) = Assert.Single(_host.Enlisted);
            Assert.Equal(ConcurrencyMode.Check, options.Concurrency);
            Assert.Null(row.RoleMemberships);
            Assert.Equal(("Ada L.", "ada@example.com", Stored), (row.Name, row.Email, row.ModifiedAt));
            Assert.Equal("ContactEmail", options.MapPath!(ValidationPath.Root.Index(0).Property("ContactEmail")).ToString());

            // --- The envelope: the saved row, the first batch's bag, the persist's tags ---
            Assert.Equal((UsersKit.CallerId, "Ada L.", "+971501234567"), (result.User.Id, result.User.Name, result.User.ContactMobile));
            Assert.Equal(new Dictionary<string, string> { ["nav.pinned"] = "[1]", ["theme"] = "dark" }, result.Preferences);
            Assert.Equal(VersionTagWire.Preferences(new VersionTag(RecordingOpenHost.PreferencesAfter)), result.PreferencesTag);
            Assert.Equal(result.PreferencesTag, result.Tags[VersionTagWire.PreferencesItem]);
        }

        // --- The envelope ---

        [Fact]
        public void The_envelope_carries_the_four_tags_as_the_header_does_and_one_summary_per_allowed_securable()
        {
            VersionTagSnapshot tenant = Tenant();
            UserVersionTagSnapshot caller = new(UsersKit.TenantId, UsersKit.CallerId, new VersionTag(Guid.NewGuid()), new VersionTag(Guid.NewGuid()), UsersKit.Now);
            AccessDecision[] decisions =
            [
                new AccessDecision("core.User", "Read", AccessOutcome.Filtered, FilterTree.Leaf("Id = me()"), [], []),
                AccessDecision.Deny("core.User", "Save"),
                new AccessDecision("gl.Center", "Save", AccessOutcome.Unrestricted, null, [], []),
            ];

            MeResult result = MeEnvelope.Compose(MeEnvelope.View(StoredRow()), new Dictionary<string, string>(), tenant, caller, decisions, new UserAccess { TenantId = UsersKit.TenantId, UserId = UsersKit.CallerId }, UsersKit.Now);

            Assert.Equal(string.Join(", ", result.Tags.Select(static tag => $"{tag.Key}={tag.Value}")), VersionTagWire.HeaderValue(tenant, caller));
            Assert.Equal([VersionTagWire.SettingsItem, VersionTagWire.PermissionsItem, VersionTagWire.PreferencesItem, VersionTagWire.EntitiesItem], result.Tags.Keys);
            Assert.Equal(result.Tags[VersionTagWire.PermissionsItem], result.Access.Tag);
            Assert.Equal(result.Tags[VersionTagWire.PreferencesItem], result.PreferencesTag);
            Assert.Equal(MeResult.FormatVersion, result.Access.FormatVersion);
            Assert.Equal([new SecurableSummary("core.User", "Read", true), new SecurableSummary("gl.Center", "Save", false)], result.Access.Securables);
            Assert.False(result.Access.IsSystem);
            Assert.Null(result.Access.ValidUntil);
        }

        // --- Helpers ---

        /// <summary>The assignments of a locale change against the kit's languages and value sets.</summary>
        /// <param name="request">The change.</param>
        /// <returns>The assignments.</returns>
        private IReadOnlyDictionary<string, object?> Assign(MeLocaleRequest request)
        {
            return UserService<User>.LocaleAssignments(request, _kit.Languages, _kit.ValueSets);
        }

        /// <summary>The refusals of a locale change, each as its path and code.</summary>
        /// <param name="request">The change.</param>
        /// <returns>The refusals.</returns>
        private (string Path, string Code)[] Refusals(MeLocaleRequest request)
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Assign(request));
            return [.. refused.Errors.Select(static error => (error.Path.ToString(), error.Code))];
        }

        /// <summary>Assignments as sorted pairs.</summary>
        /// <param name="assignments">The assignments.</param>
        /// <returns>The pairs, by property name.</returns>
        private static (string, object?)[] Sorted(IReadOnlyDictionary<string, object?> assignments)
        {
            return [.. assignments.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => (pair.Key, pair.Value))];
        }

        /// <summary>The caller's row as stored: every member set, last modified by user 5.</summary>
        /// <returns>The row.</returns>
        private static User StoredRow()
        {
            return new User
            {
                Id = UsersKit.CallerId,
                Kind = UserKind.Individual,
                State = UserState.Joined,
                IsActive = true,
                Email = "ada@example.com",
                Name = "Ada",
                PreferredLanguage = "ar",
                PreferredCalendar = "uq",
                PreferredTimeZone = "Asia/Riyadh",
                PreferredRegion = "SA",
                PreferredDigits = DigitStyle.Native,
                ModifiedAt = Stored,
                ModifiedById = 5,
                RoleMemberships = [new RoleMembership { RoleId = 3 }],
            };
        }

        /// <summary>A profile edit of the stored row.</summary>
        /// <param name="stamp">The stamp the edit was made against.</param>
        /// <param name="concurrency">How a moved stamp is treated.</param>
        /// <returns>The request.</returns>
        private static MeSaveRequest Request(DateTimeOffset stamp, ConcurrencyMode concurrency = ConcurrencyMode.Check)
        {
            return new MeSaveRequest("Ada L.", null, null, null, null, Gender.Female, null, "+971501234567", stamp, concurrency);
        }

        /// <summary>The tenant's tags.</summary>
        /// <returns>The snapshot.</returns>
        internal static VersionTagSnapshot Tenant()
        {
            return new VersionTagSnapshot(
                UsersKit.TenantId,
                new Dictionary<string, VersionTag>(StringComparer.Ordinal)
                {
                    [VersionTagNames.Settings] = new(Guid.NewGuid()),
                    [VersionTagNames.Permissions] = new(Guid.NewGuid()),
                    [VersionTagNames.Entities] = new(Guid.NewGuid()),
                },
                UsersKit.Now);
        }

        /// <summary>The read of a modifier's names: one row, or none.</summary>
        /// <param name="names">The names, all three, or none for no row.</param>
        /// <returns>The read.</returns>
        internal static RawResult Names(params string?[] names)
        {
            QueryColumn[] columns = [Text("Name"), Text("Name2"), Text("Name3")];
            return new RawResult([Rows(columns, names.Length == 0 ? [] : [[.. names]])]);
        }

        /// <summary>A nullable text column.</summary>
        /// <param name="name">The column's name.</param>
        /// <returns>The column.</returns>
        internal static QueryColumn Text(string name)
        {
            return new QueryColumn(name, QueryexType.QxString, null, null, true, null, false);
        }

        /// <summary>A result set of text columns.</summary>
        /// <param name="columns">The columns.</param>
        /// <param name="rows">The rows' values, column by column.</param>
        /// <returns>The set.</returns>
        internal static QueryRowSet Rows(QueryColumn[] columns, IReadOnlyList<object?[]> rows)
        {
            using DataTable table = new();
            foreach (QueryColumn column in columns)
            {
                _ = table.Columns.Add(column.Name, typeof(string));
            }

            foreach (object?[] row in rows)
            {
                _ = table.Rows.Add([.. row.Select(static value => value ?? DBNull.Value)]);
            }

            QueryRowSet.Builder builder = new(columns, [.. columns.Select(static _ => typeof(string))], rows.Count);
            using DataTableReader reader = table.CreateDataReader();
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return builder.Build();
        }
    }

    /// <summary>
    ///     A guarded runner that composes each batch against a batch answering a profile edit's first
    ///     round trip: the caller's stored row, its last modifier's names and its bag.
    /// </summary>
    internal sealed class SelfServiceRunner : IGuardedBatchRunner
    {
        /// <summary>The caller's row as stored.</summary>
        public User? Row { get; set; }

        /// <summary>The last modifier's names.</summary>
        public (string? Name, string? Name2, string? Name3) Modifier { get; set; } = ("Ann", null, null);

        /// <summary>The caller's bag.</summary>
        public (string Key, string Value)[] Bag { get; set; } = [];

        /// <summary>Every run's purpose, in call order.</summary>
        public ConcurrentQueue<BatchPurpose> Purposes { get; } = new();

        /// <inheritdoc />
        public async Task<TResult> RunAsync<TResult>(
            BatchPurpose purpose,
            Func<ConnectedUser, IDataBatch, ValueTask> compose,
            Func<ConnectedUser, BatchOutcome, TResult> read,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(compose);
            ArgumentNullException.ThrowIfNull(read);
            Purposes.Enqueue(purpose);

            // --- Compose against the answering batch, then complete each statement in order: the
            // modifier's names, then the bag ---
            var batch = (SelfServiceBatchProxy)(object)DispatchProxy.Create<IDataBatch, SelfServiceBatchProxy>();
            batch.Row = Row;
            ConnectedUser caller = Caller();
            await compose(caller, (IDataBatch)(object)batch).ConfigureAwait(false);
            batch.Complete(
                UserSelfServiceTests.Names(Modifier.Name, Modifier.Name2, Modifier.Name3),
                new RawResult([UserSelfServiceTests.Rows([UserSelfServiceTests.Text("Key"), UserSelfServiceTests.Text("Value")], [.. Bag.Select(static item => new object?[] { item.Key, item.Value })])]));
            return read(caller, new BatchOutcome(UserSelfServiceTests.Tenant(), null, 1));
        }

        /// <summary>The connected caller: an individual without grants.</summary>
        /// <returns>The caller.</returns>
        private static ConnectedUser Caller()
        {
            return new ConnectedUser(
                UsersKit.CallerId,
                UserKind.Individual,
                UserState.Joined,
                new UserProfile("Ada", null, null, "ada@example.com", null, null, null, null, null, null, null, null, null, null, UsersKit.Now),
                Guid.Empty,
                new UserAccess { TenantId = UsersKit.TenantId, UserId = UsersKit.CallerId },
                new Dictionary<string, Guid>(StringComparer.Ordinal),
                null);
        }
    }

    /// <summary>A batch that answers the caller's own row with a scripted row and records each raw statement for the runner to complete.</summary>
    public class SelfServiceBatchProxy : DispatchProxy
    {
        /// <summary>The raw statements' pending results, in order.</summary>
        private readonly List<BatchResult<RawResult>> _statements = [];

        /// <summary>The row the entity query answers.</summary>
        internal User? Row { get; set; }

        /// <summary>Completes the raw statements in order.</summary>
        /// <param name="results">The results.</param>
        internal void Complete(params RawResult[] results)
        {
            Assert.Equal(results.Length, _statements.Count);
            for (int index = 0; index < results.Length; index++)
            {
                _statements[index].Complete(results[index]);
            }
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            switch (targetMethod.Name)
            {
                case nameof(IDataBatch.Tvp):
                    return new SqlIdentifier("@tm_self_t");
                case nameof(IDataBatch.Query):
                    BatchResult<EntityQueryResult<User>> rows = new();
                    rows.Complete(new EntityQueryResult<User>(Row is null ? [] : [Row], new RelatedEntities()));
                    return rows;
                case nameof(IDataBatch.Sql):
                    BatchResult<RawResult> result = new();
                    _statements.Add(result);
                    return result;
                default:
                    throw new NotSupportedException($"The profile edit reached {targetMethod.Name}, which the answering batch does not answer.");
            }
        }
    }

    /// <summary>An open frame that records each enlisted save and answers each persist with fixed tags.</summary>
    internal sealed class RecordingOpenHost : IOpenWriteHost, IEnlistmentHost
    {
        /// <summary>The caller's preferences tag after a persist.</summary>
        public static readonly Guid PreferencesAfter = new("5e1f0000-0000-0000-0000-000000000001");

        /// <summary>Every enlisted row with its options, in order.</summary>
        public List<(User Row, EnlistSaveOptions Options)> Enlisted { get; } = [];

        /// <summary>How many times the frame persisted.</summary>
        public int Persists { get; private set; }

        /// <inheritdoc />
        public WriteHostKind Kind => WriteHostKind.ApiAction;

        /// <inheritdoc />
        public string Operation => "me/save";

        /// <inheritdoc />
        public IConfirmations Confirmations => throw new NotSupportedException("A profile edit asks for no confirmations here.");

        /// <inheritdoc />
        public Task<EnlistmentOutcome> PersistAsync(CancellationToken cancellationToken = default)
        {
            Persists++;
            return Task.FromResult(new EnlistmentOutcome(UsersKit.Now, UserSelfServiceTests.Tenant(), new UserVersionTagSnapshot(UsersKit.TenantId, UsersKit.CallerId, new VersionTag(PreferencesAfter), new VersionTag(Guid.Empty), UsersKit.Now)));
        }

        /// <inheritdoc />
        public Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(IEnlists<TTarget> writer, IReadOnlyList<TTarget> rows, EnlistSaveOptions options)
        {
            foreach (TTarget row in rows)
            {
                Enlisted.Add(((User)(object)row!, options));
            }

            return Task.FromResult(new EnlistedSave<TTarget>(rows, "core.User"));
        }

        /// <inheritdoc />
        public EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(IEnlists<TTarget> writer, IReadOnlyList<TKey> ids, EnlistDeleteOptions options)
            where TTarget : Entity<TKey>
            where TKey : struct, System.Numerics.IBinaryInteger<TKey>
        {
            throw new NotSupportedException("A profile edit deletes nothing.");
        }
    }
}
