// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;
using Tellma.Core.Jobs;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     A job's display text and the handlers' client metadata over the job tests' feature and
    ///     the test assembly's texts: the job's own label first, then the handler's label resource,
    ///     then the handler key; the <c>entity</c> argument as the named entity's plural label; and
    ///     the <c>jobHandlers</c> section, whose fallback is sent only for a handler with no label
    ///     resource.
    /// </summary>
    public sealed class JobLabelsTests : IDisposable
    {
        /// <summary>The job tests' feature with one more handler, which commits in steps and leaves a row.</summary>
        private readonly ServiceProvider _provider = TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.D>("acme", contribute: static contribution => JobTestModel
                .Contribute(contribution)
                .JobHandler<ExportHandler>())))
            .BuildServiceProvider(validateScopes: true);

        /// <summary>A British English reader in UTC.</summary>
        private static LocalizationContext English { get; } = new(NegotiatedCultures.Get("en", "GB"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, [], "en", "en-GB");

        /// <summary>A Saudi Arabic reader in UTC.</summary>
        private static LocalizationContext Arabic { get; } = new(NegotiatedCultures.Get("ar", "SA"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, [], "ar", "ar-SA");

        /// <summary>The renderer under test.</summary>
        private JobLabels Labels => _provider.GetRequiredService<JobLabels>();

        // --- The render order ---

        [Fact]
        public void The_jobs_own_label_renders_first_with_its_entity_argument_as_the_entitys_plural_label()
        {
            string text = Labels.Render("acme.recompute-balances", "Acme.Job.Exporting", /*lang=json,strict*/ """{"entity":"jobs.Shipment","customer":"Contoso"}""", English);

            Assert.Equal("Exporting Shipments for Contoso", text);
        }

        [Fact]
        public void An_entity_argument_naming_no_entity_renders_as_it_is()
        {
            string text = Labels.Render("acme.recompute-balances", "Acme_Job_Exporting", /*lang=json,strict*/ """{"entity":"Invoices","customer":"Contoso"}""", English);

            Assert.Equal("Exporting Invoices for Contoso", text);
        }

        [Fact]
        public void A_label_no_resource_declares_falls_to_the_handlers_label_in_the_readers_language()
        {
            Assert.Equal("Recompute the balances", Labels.Render("acme.recompute-balances", "Acme.Job.Forgotten", null, English));
            Assert.Equal("إعادة حساب الأرصدة", Labels.Render("acme.recompute-balances", null, null, Arabic));
        }

        [Fact]
        public void A_handler_with_no_label_resource_renders_its_key()
        {
            Assert.Equal("acme.ship", Labels.Render(new Job { HandlerKey = "acme.ship" }, English));
        }

        [Fact]
        public void A_platform_handlers_label_is_cores_own()
        {
            Assert.Equal("Clean up old jobs", Labels.Render(JobRetentionHandler.Key, null, null, English));
            Assert.Equal("فحص الأشجار", Labels.Render(TreeVerifyHandler.Key, null, null, Arabic));
        }

        // --- The jobHandlers section ---

        [Fact]
        public void The_section_lists_every_handler_by_key_with_a_fallback_only_where_no_resource_declares_the_label()
        {
            ClientAssetContext context = new(_provider);
            _provider.GetServices<IClientAssetContributor>().OfType<JobHandlersClientAssetContributor>().Single().Contribute(context);

            ClientMetadataSection section = Assert.Single(context.Sections);
            Assert.Equal((ClientMetadataChunk.Core, "jobHandlers"), (section.Chunk, section.Name));
            Assert.Equal(
                /*lang=json,strict*/ """[{"key":"acme.export","schedulable":false,"commitsInSteps":true,"labelKey":"JobHandler_acme_export","labelFallback":"acme.export","result":{"entity":"jobs.Shipment","via":"JobId"}},{"key":"acme.inspect","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_acme_inspect","labelFallback":"acme.inspect"},{"key":"acme.recompute-balances","schedulable":true,"commitsInSteps":false,"labelKey":"JobHandler_acme_recompute-balances"},{"key":"acme.ship","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_acme_ship","labelFallback":"acme.ship"},{"key":"core.blob-reconcile","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_core_blob-reconcile"},{"key":"core.blob-sweep","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_core_blob-sweep"},{"key":"core.job-retention","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_core_job-retention"},{"key":"core.notification-delivery","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_core_notification-delivery"},{"key":"core.notification-retention","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_core_notification-retention"},{"key":"core.outgoing-email","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_core_outgoing-email"},{"key":"core.tree-verify","schedulable":false,"commitsInSteps":false,"labelKey":"JobHandler_core_tree-verify"}]""",
                Encoding.UTF8.GetString(section.Json.Span));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
        }

        /// <summary>A handler that commits in steps and leaves a shipment for the user.</summary>
        [JobHandler("acme.export", CommitsInSteps = true, ResultEntity = "jobs.Shipment", ResultVia = nameof(Shipment.JobId))]
        public sealed class ExportHandler : IJobHandler
        {
            /// <inheritdoc />
            public Task ExecuteAsync(JobBatch<Job> batch)
            {
                return Task.CompletedTask;
            }
        }
    }
}
