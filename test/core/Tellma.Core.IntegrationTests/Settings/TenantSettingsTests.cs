// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Calendars;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Settings;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.IntegrationTests.Settings
{
    /// <summary>
    ///     Tenant settings against a real database: the placeholder the migrations seed, the routing
    ///     guard, the cold load and the warm path, the typed columns and both views, history, the
    ///     provisioning step, and the Umm al-Qura months.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class TenantSettingsTests(DataBatchDatabase fixture) : IClassFixture<DataBatchDatabase>, IAsyncLifetime
    {
        /// <summary>Puts the row back to the placeholder the migrations seed, and removes every entry.</summary>
        private const string ResetSettingsText = """
            DELETE FROM [core].[SettingEntries];
            UPDATE [core].[Settings]
               SET [TenantId] = 0, [Name] = N'', [Name2] = NULL, [Name3] = NULL, [PrimaryLanguage] = 'en', [SecondaryLanguage] = NULL,
                   [TertiaryLanguage] = NULL, [PrimaryCalendar] = 'gc', [SecondaryCalendar] = NULL, [TimeZone] = 'UTC', [Region] = NULL, [Digits] = NULL;
            """;

        /// <summary>The fixture tenant as the registry describes it.</summary>
        private static readonly TenantDescriptor Descriptor = new(FixtureTenant.TenantId, "Fixture", null, null, ["en"], TenantCategory.Live, TenantState.Active, null);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The process-wide settings store of the fixture tenant's host.</summary>
        private TenantSettingsStore Store => fixture.Tenant.Services.GetRequiredService<TenantSettingsStore>();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
            await fixture.SeedTagsAsync();
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, ResetSettingsText);

            // The store outlives the tests of the class; each starts cold.
            Store.Remove(new TenantCacheKey(FixtureTenant.TenantId));
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        // --- The placeholder and the routing guard ---

        [Fact]
        public async Task The_seeded_placeholder_serves_the_platforms_own_work_and_is_never_cached()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            ITenantSettingsCache cache = scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>();

            CacheResult<TenantSettings> first = await cache.GetAsync();
            CacheResult<TenantSettings> second = await cache.GetAsync();

            Assert.Equal((CacheOutcome.Uncached, CacheOutcome.Uncached), (first.Outcome, second.Outcome));
            Assert.Equal(VersionTag.None, first.Tag);
            Assert.Equal(["en"], first.Value!.Languages.Select(static language => language.Code));
            Assert.Equal(["gc"], first.Value.Calendars.Select(static calendar => calendar.Code));
            Assert.Equal("UTC", first.Value.SqlServerTimeZoneName);
        }

        [Fact]
        public async Task The_placeholder_fails_closed_for_a_user()
        {
            await using AsyncServiceScope scope = UserScope();

            TenantDatabaseMismatchException failure = await Assert.ThrowsAsync<TenantDatabaseMismatchException>(
                () => scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>().GetAsync());

            Assert.Equal((FixtureTenant.TenantId, 0), (failure.ExpectedTenantId, failure.ActualTenantId));
        }

        [Fact]
        public async Task A_database_of_another_tenant_fails_closed_and_is_logged_as_critical()
        {
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, "UPDATE [core].[Settings] SET [TenantId] = 42;");
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();

            TenantDatabaseMismatchException failure = await Assert.ThrowsAsync<TenantDatabaseMismatchException>(
                () => scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>().GetAsync());

            Assert.Equal((FixtureTenant.TenantId, 42), (failure.ExpectedTenantId, failure.ActualTenantId));
            Assert.Contains(fixture.Logs.GetSnapshot(), static record => record.Level == LogLevel.Critical && record.Message.Contains("42", StringComparison.Ordinal));
        }

        // --- The cold load and the warm path ---

        [Fact]
        public async Task A_cold_load_reads_the_row_in_one_round_trip_and_the_warm_path_costs_none()
        {
            await CompleteAsync();
            fixture.ResetRoundTrips();

            // --- Cold: one read batch, the prelude and the two statements ---
            TenantSettings loaded;
            VersionTag tag;
            await using (AsyncServiceScope scope = UserScope())
            {
                CacheResult<TenantSettings> cold = await scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>().GetAsync();
                Assert.Equal(CacheOutcome.Miss, cold.Outcome);
                Assert.Equal(1L, fixture.ServerRoundTrips);
                loaded = cold.Value!;
                tag = cold.Tag;
            }

            Assert.Equal((await fixture.TagsAsync())[VersionTagNames.Settings], tag.Value);
            Assert.Equal(tag, loaded.Tag);

            // --- Warm: the same instance, from memory ---
            fixture.ResetRoundTrips();
            await using AsyncServiceScope next = UserScope();
            ITenantSettingsCache cache = next.ServiceProvider.GetRequiredService<ITenantSettingsCache>();
            CacheResult<TenantSettings> warm = await cache.GetAsync();
            Assert.Equal(CacheOutcome.Hit, warm.Outcome);
            Assert.Same(loaded, warm.Value);
            Assert.Same(loaded, cache.Peek());
            Assert.Equal(0L, fixture.ServerRoundTrips);
        }

        [Fact]
        public async Task The_typed_columns_and_the_entries_reach_both_views()
        {
            await CompleteAsync(
                "[SecondaryCalendar] = 'uq', [TimeZone] = 'Asia/Riyadh', [Region] = 'SA', [Digits] = 'Native'",
                "('fixture.unknown', N'true')");
            await using AsyncServiceScope scope = UserScope();
            ITenantSettingsCache cache = scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>();

            // --- The server's view ---
            TenantSettings settings = (await cache.GetAsync()).Value!;
            Assert.Equal(FixtureTenant.TenantId, settings.TenantId);
            Assert.Equal(["Fixture", null, null], settings.Names);
            Assert.Equal(["gc", "uq"], settings.Calendars.Select(static calendar => calendar.Code));
            Assert.Equal(("Asia/Riyadh", "Arab Standard Time"), (settings.TimeZone.Id, settings.SqlServerTimeZoneName));
            Assert.Equal(("SA", DigitStyle.Native), (settings.Region, settings.Digits));
            Assert.Empty(settings.Entries);

            // --- The client's view, under the wire tag of the same tag ---
            (byte[] json, string wireTag) = await cache.GetForClientAsync();
            using var document = JsonDocument.Parse(json);
            Assert.Equal(settings.Tag.ToWire(TenantSettingsForClient.FormatVersion), wireTag);
            Assert.Equal("Asia/Riyadh", document.RootElement.GetProperty("timeZone").GetString());
            Assert.Equal("SA", document.RootElement.GetProperty("region").GetString());
            Assert.Equal("Native", document.RootElement.GetProperty("digits").GetString());
            Assert.Equal(["gc", "uq"], document.RootElement.GetProperty("calendars").EnumerateArray().Select(static code => code.GetString()));
        }

        // --- Writes ---

        [Fact]
        public async Task The_entries_keep_history_of_every_change()
        {
            await CompleteAsync(entries: "('fixture.flag', N'true')");

            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, "UPDATE [core].[SettingEntries] SET [Value] = N'false';");

            Assert.Equal(1, await CountAsync("[core].[SettingEntriesHistory]"));
        }

        // --- Provisioning ---

        [Fact]
        public async Task Provisioning_completes_the_placeholder_once_through_the_emitter_and_leaves_a_completed_row_alone()
        {
            int history = await CountAsync("[core].[SettingsHistory]");
            Guid before = (await fixture.TagsAsync())[VersionTagNames.Settings];
            await using AsyncServiceScope scope = await SystemScopeAsync();
            SettingsProvisioningStep step = Step(scope, new TellmaSeedOptions());
            TenantDescriptor tenant = Descriptor with { Name2 = "فكستشر" };

            await step.RunAsync(new TenantProvisioningContext(tenant, scope.ServiceProvider, IsNew: true, null, null));

            TypedSettings row = await ReadRowAsync(scope.ServiceProvider.GetRequiredService<ITenantDatabase>());
            Assert.Equal((FixtureTenant.TenantId, "Fixture"), (row.TenantId, row.Name));

            // The fixture offers English alone, so the shape has no Name2 to query and the step writes none.
            Assert.Equal(0, await CountAsync("[core].[Settings] WHERE [Name2] IS NOT NULL"));
            Assert.Equal(("en", "gc", "UTC"), (row.PrimaryLanguage, row.PrimaryCalendar, row.TimeZone));

            // The save keeps the placeholder in history and moves the settings tag; the tag's own write adds no history row.
            Assert.Equal(history + 1, await CountAsync("[core].[SettingsHistory]"));
            Assert.NotEqual(before, (await fixture.TagsAsync())[VersionTagNames.Settings]);

            // A second run, after an administrator's change, keeps the change.
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, "UPDATE [core].[Settings] SET [TimeZone] = 'Asia/Riyadh';");
            await step.RunAsync(new TenantProvisioningContext(tenant, scope.ServiceProvider, IsNew: false, null, null));
            Assert.Equal("Asia/Riyadh", (await ReadRowAsync(scope.ServiceProvider.GetRequiredService<ITenantDatabase>())).TimeZone);
        }

        [Fact]
        public async Task Provisioning_takes_a_seeded_tenants_calendars_and_refuses_a_seed_the_editor_would()
        {
            await using AsyncServiceScope scope = await SystemScopeAsync();
            TenantDescriptor tenant = Descriptor;

            // --- A seed the editor would refuse fails the step and writes nothing ---
            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Step(scope, Seed(languages: ["en"], calendars: ["uq", "uq"])).RunAsync(new TenantProvisioningContext(tenant, scope.ServiceProvider, true, null, null)));
            Assert.Contains("'uq' twice", refused.Message, StringComparison.Ordinal);
            Assert.Equal(0, (await ReadRowAsync(scope.ServiceProvider.GetRequiredService<ITenantDatabase>())).TenantId);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Step(scope, Seed(languages: ["zz"], calendars: [])).RunAsync(new TenantProvisioningContext(tenant, scope.ServiceProvider, true, null, null)));

            // --- A sound seed: its calendars in order ---
            await Step(scope, Seed(languages: ["en"], calendars: ["uq", "gc"])).RunAsync(new TenantProvisioningContext(tenant, scope.ServiceProvider, true, null, null));
            TypedSettings row = await ReadRowAsync(scope.ServiceProvider.GetRequiredService<ITenantDatabase>());
            Assert.Equal(("en", "uq", "gc"), (row.PrimaryLanguage, row.PrimaryCalendar, row.SecondaryCalendar));
        }

        // --- The Umm al-Qura months ---

        [Fact]
        public async Task The_migrations_seed_every_umm_al_qura_month_as_the_calendar_computes_it()
        {
            List<(string Calendar, int Year, int Month, DateOnly Start, DateOnly End)> stored = [];
            await using (SqlConnection connection = new(fixture.Tenant.ConnectionString))
            {
                await connection.OpenAsync(Token);
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT [Calendar], [Year], [Month], [StartDate], [EndDate] FROM [core].[CalendarMonths] ORDER BY [Calendar], [Year], [Month];";
                await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
                while (await reader.ReadAsync(Token))
                {
                    stored.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4)));
                }
            }

            Assert.Equal(
                CalendarMonthRowConfiguration.UmAlQuraMonths().Select(static month => (month.Calendar, month.Year, month.Month, month.StartDate, month.EndDate)),
                stored);
        }

        /// <summary>A scope of the fixture tenant bound to a user, as a request's is.</summary>
        /// <returns>The scope.</returns>
        private AsyncServiceScope UserScope()
        {
            AsyncServiceScope scope = fixture.Tenant.Services.CreateAsyncScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            holder.Set(holder.Current with { Tenant = Descriptor, Kind = PrincipalKind.User, Subject = "fx-user", UserId = DataBatchDatabase.CallerId });
            return scope;
        }

        /// <summary>
        ///     A scope of the platform's own work for the fixture tenant, its context carrying the
        ///     tenant's settings as the locale negotiation leaves them, as the provisioning runner's is.
        /// </summary>
        /// <returns>The scope.</returns>
        private async Task<AsyncServiceScope> SystemScopeAsync()
        {
            AsyncServiceScope scope = fixture.Tenant.CreateScope(WellKnownIds.SystemUserId);
            TenantSettings settings = (await scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>().GetAsync()).Value!;
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            holder.Set(holder.Current with { TenantSettings = settings });
            return scope;
        }

        /// <summary>Completes the placeholder for the fixture tenant, as provisioning would, with some columns and entries.</summary>
        /// <param name="assignments">More column assignments; none when null.</param>
        /// <param name="entries">Entry rows to insert, <c>(key, value)</c> each; none when null.</param>
        /// <returns>A task that completes when the row is written.</returns>
        private async Task CompleteAsync(string? assignments = null, string? entries = null)
        {
            string text = string.Create(
                CultureInfo.InvariantCulture,
                $"UPDATE [core].[Settings] SET [TenantId] = {FixtureTenant.TenantId}, [Name] = N'Fixture'{(assignments is null ? string.Empty : ", " + assignments)};");
            if (entries is not null)
            {
                text += string.Create(
                    CultureInfo.InvariantCulture,
                    $"\nINSERT INTO [core].[SettingEntries] ([Id], [Key], [Value], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) SELECT NEXT VALUE FOR [core].[sq_SettingEntries], v.[Key], v.[Value], SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId} FROM (VALUES {entries}) AS v ([Key], [Value]);");
            }

            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, text);
        }

        /// <summary>The settings row as the platform reads it.</summary>
        /// <param name="database">The scope's database.</param>
        /// <returns>The row.</returns>
        private static async Task<TypedSettings> ReadRowAsync(ITenantDatabase database)
        {
            IDataBatch read = database.CreateBatch(BatchPurpose.Read);
            BatchResult<EntityQueryResult<TypedSettings>> result = read.Query(new EntityQuery<TypedSettings>
            {
                Select = "Id,TenantId,Name,PrimaryLanguage,PrimaryCalendar,SecondaryCalendar,TimeZone,ModifiedAt",
            });
            _ = await read.ExecuteAsync(Token);
            return Assert.Single(result.Value.Entities);
        }

        /// <summary>Counts a table's rows.</summary>
        /// <param name="table">The table, bracketed, optionally followed by a WHERE clause.</param>
        /// <returns>The count.</returns>
        private async Task<int> CountAsync(string table)
        {
            await using SqlConnection connection = new(fixture.Tenant.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (int)await command.ExecuteScalarAsync(Token);
        }

        /// <summary>The provisioning step over the fixture host's language catalogue and calendars.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="seed">The seed.</param>
        /// <returns>The step.</returns>
        private static SettingsProvisioningStep Step(AsyncServiceScope scope, TellmaSeedOptions seed)
        {
            return new SettingsProvisioningStep(
                scope.ServiceProvider.GetRequiredService<ILanguageCatalog>(),
                scope.ServiceProvider.GetRequiredService<ICalendarRegistry>(),
                Options.Create(seed));
        }

        /// <summary>A seed listing the fixture tenant with its languages and calendars.</summary>
        /// <param name="languages">The languages.</param>
        /// <param name="calendars">The calendars.</param>
        /// <returns>The seed.</returns>
        private static TellmaSeedOptions Seed(string[] languages, string[] calendars)
        {
            TellmaSeedTenant tenant = new() { Id = FixtureTenant.TenantId, Name = "Fixture" };
            foreach (string language in languages)
            {
                tenant.Languages.Add(language);
            }

            foreach (string calendar in calendars)
            {
                tenant.Calendars.Add(calendar);
            }

            TellmaSeedOptions seed = new();
            seed.Tenants.Add(tenant);
            return seed;
        }
    }
}
