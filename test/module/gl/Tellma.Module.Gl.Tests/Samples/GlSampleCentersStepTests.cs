// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using static Tellma.Module.Gl.Tests.GlTestKit;

namespace Tellma.Module.Gl.Tests.Samples
{
    /// <summary>
    ///     The sample-centers step over a recording pipeline: a replacement's rows written in one read
    ///     and one save, stored centers updated under their ids and new ones referenced by temporary
    ///     ids; nothing written for an empty list or outside Development; a list that cannot be written
    ///     refused before anything is read; and names mapped onto the tenant's language slots.
    /// </summary>
    public class GlSampleCentersStepTests
    {
        private static readonly LanguageInfo English = new("en", "English", "EN", false);
        private static readonly LanguageInfo Arabic = new("ar", "العربية", "ع", true);
        private static readonly LanguageInfo French = new("fr", "Français", "FR", false);
        private static readonly string[] ArgumentNames = ["c0", "c1", "c2"];

        private readonly FakeLogger<GlSampleCentersStep<Center>> _logger = new();

        [Fact]
        public void The_step_is_named_ordered_and_versioned_by_its_centers()
        {
            GlSampleCentersStep<Center> step = Step(new Samples(7), "Development");

            Assert.Equal(("gl.sample-centers", 100, 7), (step.Name, step.Order, step.Version));
        }

        [Fact]
        public async Task A_replacements_rows_are_written_in_one_read_and_one_save()
        {
            Samples samples = new(
                2,
                Sample("ROOT", CenterType.Abstract, null, ("en", "Root"), ("ar", "الجذر")),
                Sample("UNIT", CenterType.BusinessUnit, "ROOT", ("en", "Unit"), ("ar", "الوحدة")),
                Sample("SALES", CenterType.Sale, "UNIT", ("en", "Sales"), ("ar", "المبيعات")));
            RecordingPipeline<Center, int> pipeline = new(new Dictionary<string, int> { ["unit"] = 50 });

            await Step(samples, "Development").RunAsync(Context(pipeline, English, Arabic));

            // --- One read of the list's codes, inactive centers included ---
            Assert.Equal(["query", "save"], pipeline.Calls);
            QueryRequest query = Assert.Single(pipeline.Queries);
            Assert.Equal("Id, Code", query.Select);
            Assert.Equal(["Code in (@c0, @c1, @c2)"], query.Filters);
            Assert.Equal(["ROOT", "UNIT", "SALES"], ArgumentNames.Select(name => query.Arguments![name].GetString()));
            Assert.Equal((3, true), (query.Take, query.IncludeInactive));

            // --- One save as the system: the stored center under its id, new ones and their references by temporary ids ---
            (IReadOnlyList<Center> rows, SaveOptions options) = Assert.Single(pipeline.Saves);
            Assert.Equal((SaveSource.System, false, ConcurrencyMode.Override), (options.Source, options.ReturnEntities, options.Concurrency));
            Assert.Equal(
                [(-1, null, CenterType.Abstract, "ROOT"), (50, -1, CenterType.BusinessUnit, "UNIT"), (-3, 50, CenterType.Sale, "SALES")],
                rows.Select(static row => (row.Id, row.ParentId, row.CenterType, row.Code)));
            Assert.Equal([("Root", "الجذر"), ("Unit", "الوحدة"), ("Sales", "المبيعات")], rows.Select(static row => (row.Name, row.Name2)));
            Assert.All(rows, static row => Assert.Null(row.Name3));

            // --- The event, with the tenant and the count ---
            FakeLogRecord record = Assert.Single(_logger.Collector.GetSnapshot());
            Assert.Equal("SampleCentersProvisioned", record.Id.Name);
            Assert.Equal(("1", "3"), (State(record, "TenantId"), State(record, "Count")));
        }

        [Fact]
        public async Task An_empty_list_writes_nothing_and_reads_nothing()
        {
            RecordingPipeline<Center, int> pipeline = new(new Dictionary<string, int>());

            await Step(new Samples(1), "Development").RunAsync(Context(pipeline, English));

            Assert.Empty(pipeline.Calls);
            Assert.Empty(_logger.Collector.GetSnapshot());
        }

        [Fact]
        public async Task Outside_development_the_step_writes_nothing_whatever_the_list()
        {
            RecordingPipeline<Center, int> pipeline = new(new Dictionary<string, int>());
            Samples broken = new(1, Sample("A", CenterType.Sale, "MISSING", ("en", "A")));

            await Step(broken, "Production").RunAsync(Context(pipeline, English));
            await Step(new Samples(1, Sample("A", CenterType.Abstract, null, ("en", "A"))), "Staging").RunAsync(Context(pipeline, English));

            Assert.Empty(pipeline.Calls);
        }

        [Fact]
        public async Task An_unknown_parent_code_is_refused_before_anything_is_read_or_written()
        {
            RecordingPipeline<Center, int> pipeline = new(new Dictionary<string, int>());
            Samples samples = new(1, Sample("A", CenterType.Abstract, null, ("en", "A")), Sample("B", CenterType.Sale, "MISSING", ("en", "B")));

            InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => Step(samples, "Development").RunAsync(Context(pipeline, English)));

            Assert.Contains(typeof(Samples).FullName!, refusal.Message, StringComparison.Ordinal);
            Assert.Contains("'MISSING'", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(pipeline.Calls);
        }

        [Fact]
        public void A_center_under_itself_is_refused()
        {
            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => GlSampleCentersStep<Center>.Check([Sample("A", CenterType.Abstract, "a", ("en", "A"))], typeof(Samples)));

            Assert.Contains("'a'", refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_code_given_twice_is_refused()
        {
            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => GlSampleCentersStep<Center>.Check([Sample("A", CenterType.Abstract, null, ("en", "A")), Sample("a", CenterType.Abstract, null, ("en", "A"))], typeof(Samples)));

            Assert.Contains("more than once", refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_center_without_a_code_or_a_name_is_refused()
        {
            _ = Assert.Throws<InvalidOperationException>(() => GlSampleCentersStep<Center>.Check([Sample(" ", CenterType.Abstract, null, ("en", "A"))], typeof(Samples)));
            _ = Assert.Throws<InvalidOperationException>(() => GlSampleCentersStep<Center>.Check([Sample("A", CenterType.Abstract, null)], typeof(Samples)));
            _ = Assert.Throws<InvalidOperationException>(() => GlSampleCentersStep<Center>.Check([Sample("A", CenterType.Abstract, null, ("en", ""))], typeof(Samples)));
        }

        [Fact]
        public void More_centers_than_one_read_can_find_are_refused()
        {
            SampleCenter[] centers = [.. Enumerable.Range(0, GlSampleCentersStep<Center>.MaxCenters + 1).Select(static i => Sample("C" + i, CenterType.Abstract, null, ("en", "C")))];

            _ = Assert.Throws<InvalidOperationException>(() => GlSampleCentersStep<Center>.Check(centers, typeof(Samples)));
        }

        [Fact]
        public void The_filter_splits_its_codes_into_lists_of_at_most_128()
        {
            string filter = GlSampleCentersStep<Center>.Filter(130);

            Assert.StartsWith("Code in (@c0, @c1, ", filter, StringComparison.Ordinal);
            Assert.EndsWith(", @c127) or Code in (@c128, @c129)", filter, StringComparison.Ordinal);
        }

        [Fact]
        public void Names_fill_the_tenants_language_slots_by_subtag()
        {
            SampleCenterName[] names = [new("en", "Sales"), new("ar-SA", "المبيعات")];

            Assert.Equal(("المبيعات", "Sales", null), GlSampleCentersStep<Center>.Names(names, [Arabic, English]));
            Assert.Equal(("Sales", null, "المبيعات"), GlSampleCentersStep<Center>.Names(names, [English, French, Arabic]));
        }

        [Fact]
        public void A_primary_language_without_a_name_falls_back_to_the_first_name()
        {
            SampleCenterName[] names = [new("en", "Sales"), new("ar", "المبيعات")];

            Assert.Equal(("Sales", "المبيعات", null), GlSampleCentersStep<Center>.Names(names, [French, Arabic]));
        }

        /// <summary>A sample center.</summary>
        /// <param name="code">The code.</param>
        /// <param name="type">The type.</param>
        /// <param name="parentCode">The parent's code.</param>
        /// <param name="names">The names, by language.</param>
        /// <returns>The center.</returns>
        private static SampleCenter Sample(string code, CenterType type, string? parentCode, params (string Language, string Text)[] names)
        {
            return new SampleCenter(code, [.. names.Select(static name => new SampleCenterName(name.Language, name.Text))], type, parentCode);
        }

        /// <summary>The value of a log record's structured state.</summary>
        /// <param name="record">The record.</param>
        /// <param name="key">The state's key.</param>
        /// <returns>The value.</returns>
        private static string? State(FakeLogRecord record, string key)
        {
            return record.StructuredState!.Single(pair => pair.Key == key).Value;
        }

        /// <summary>A provisioning context whose scope serves a center service over a pipeline and settings in some languages.</summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <param name="languages">The tenant's content languages.</param>
        /// <returns>The context.</returns>
        private static TenantProvisioningContext Context(RecordingPipeline<Center, int> pipeline, params LanguageInfo[] languages)
        {
            TenantDescriptor tenant = new(1, "Acme", null, null, [.. languages.Select(static language => language.Code)], TenantCategory.Live, TenantState.Active, null);
            TenantSettings settings = new()
            {
                TenantId = 1,
                Tag = new VersionTag(Guid.Empty),
                Names = ["Acme", null, null],
                Languages = languages,
                Shape = (MultilingualShape)languages.Length,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            };

            ServiceCollection services = [];
            _ = services.AddSingleton<EntityService<Center, int>>(new CenterService<Center>(new RecordingPipelines(pipeline)));
            _ = services.AddSingleton<IRequestContextAccessor>(new Accessor(Caller with { Tenant = tenant, TenantSettings = settings }));
            return new TenantProvisioningContext(tenant, services.BuildServiceProvider(), true, null, null);
        }

        /// <summary>The step under test.</summary>
        /// <param name="samples">The sample centers.</param>
        /// <param name="environment">The deployment's environment.</param>
        /// <returns>The step.</returns>
        private GlSampleCentersStep<Center> Step(IGlSampleCenters samples, string environment)
        {
            return new GlSampleCentersStep<Center>(samples, new DeploymentIdentity("acme", environment), _logger);
        }

        /// <summary>A replacement of the default sample centers.</summary>
        /// <param name="version">The version.</param>
        /// <param name="centers">The centers.</param>
        private sealed class Samples(int version, params SampleCenter[] centers) : IGlSampleCenters
        {
            /// <inheritdoc />
            public int Version => version;

            /// <inheritdoc />
            public IReadOnlyList<SampleCenter> Centers => centers;
        }

        /// <summary>A request context accessor over one context.</summary>
        /// <param name="current">The context.</param>
        private sealed class Accessor(RequestContext current) : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current => current;
        }
    }
}
