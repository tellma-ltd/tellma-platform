// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.3: a background export runs as its requester, in the job frame the worker opens
    // around the handler. It checks the read again, writes the workbook under the request's
    // localization with the background cap, stages the file, and enlists the insert of the Export
    // row that owns it, so the row commits with the job's outcome and the staged file is confirmed
    // in the same transaction. A refusal the run can never get past fails the job without a retry.
    /// <summary>
    ///     The handler of a background export: writes the workbook a job's arguments describe, keeps
    ///     it as an export its requester downloads, and notifies the requester once it is ready.
    /// </summary>
    /// <param name="services">The job scope, whose operations of the exported resource are resolved by its name.</param>
    /// <param name="access">Checks the requester's read again.</param>
    /// <param name="exports">Writes the workbook.</param>
    /// <param name="contexts">Builds the workbook's context from the job's localization.</param>
    /// <param name="blobs">Stages the workbook.</param>
    /// <param name="host">The job frame, which the Export row's insert is enlisted with.</param>
    /// <param name="accessor">The job scope's context, whose clock dates the export's expiry.</param>
    /// <param name="options">The codec's options: how long an export is kept.</param>
    [JobHandler(
        ExcelJobs.ExportHandlerKey,
        BatchSize = 1,
        LeaseSeconds = 600,
        MaxAttempts = 3,
        ResultEntity = CoreResources.Export,
        ResultVia = nameof(Export.JobId),
        RunsWhenReadOnly = true)]
    internal sealed class ExportJobHandler(
        IServiceProvider services,
        IAccessEvaluator access,
        ExcelExports exports,
        ExcelContextFactory contexts,
        IBlobService blobs,
        IOpenWriteHost host,
        IRequestContextAccessor accessor,
        IOptions<ExcelOptions> options) : IJobHandler, IEnlists<Export>
    {
        /// <summary>The members of the Export row the handler writes though no client may.</summary>
        private static readonly IReadOnlySet<string> ServerOwned = new HashSet<string>(StringComparer.Ordinal) { nameof(Export.JobId) };

        /// <summary>The codec's options.</summary>
        private readonly ExcelOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            JobItem<Job> item = batch.Single;
            ExportJobArguments arguments = item.Arguments<ExportJobArguments>();

            // --- The requester's localization, which the workbook, its labels and its messages render in ---
            LocalizationContext localization = contexts.Localize(arguments.Culture);
            using IDisposable culture = LocalizationScope.Enter(localization);
            try
            {
                await RunAsync(item, arguments, contexts.Create(localization)).ConfigureAwait(false);
            }
            catch (Exception refused) when (Refusal(refused) is JobError error)
            {
                // A refusal no attempt gets past, which the worker's failure notice reports.
                item.Fail(error);
            }
        }

        /// <summary>
        ///     The error a refusal of the run fails the job with: a denied read, a ceiling exceeded, a
        ///     file the store refuses, a query its rows cannot compute, or a request its plan or its
        ///     clauses refuse; null for any other failure, which the worker records or retries.
        /// </summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The error, or null.</returns>
        private static JobError? Refusal(Exception failure)
        {
            // The start plans the request's columns, paths and named filters and checks its ceilings,
            // but its filters, ordering and search compile only here, and only the rows reveal a
            // cell above its length or a sheet above its cap. These arms are what refuses those,
            // and a request its stack stopped admitting while the job waited.
            return failure switch
            {
                ForbiddenException forbidden => new JobError(forbidden.Code, forbidden.Arguments),
                BadRequestException bad => new JobError(bad.Code, bad.Arguments),
                InvalidQueryException { Diagnostics: [QueryexDiagnostic first, ..] } => new JobError(first.Code, Arguments(first)),
                LimitExceededException exceeded => new JobError(exceeded.Limit, exceeded.Arguments),
                BlobRejectedException rejected => new JobError(rejected.Code, rejected.Arguments),
                ValidationException { Errors: [ValidationError { Code: ValidationCodes.QueryArithmeticFailed } arithmetic] } => new JobError(arithmetic.Code, arithmetic.Arguments),
                _ => null,
            };
        }

        /// <summary>The arguments of a query's first diagnostic, with the clause it is in.</summary>
        /// <param name="diagnostic">The diagnostic.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(QueryexDiagnostic diagnostic)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal);
            foreach ((string key, string value) in diagnostic.Arguments)
            {
                arguments[key] = value;
            }

            arguments["location"] = diagnostic.Location;
            return arguments;
        }

        /// <summary>The run's steps: the read checked, the workbook written and staged, the row enlisted and the requester notified.</summary>
        /// <param name="item">The job's item.</param>
        /// <param name="arguments">The job's arguments.</param>
        /// <param name="context">The workbook's context.</param>
        /// <returns>A task that completes once the item is marked.</returns>
        private async Task RunAsync(JobItem<Job> item, ExportJobArguments arguments, ExcelContext context)
        {
            CancellationToken cancellationToken = item.CancellationToken;

            // --- 1. The exported resource, and the requester's read of it, checked again ---
            IExcelExportTarget target = ExcelJobTargets.Export(services, arguments.Resource)
                ?? throw new InvalidOperationException($"The resource {arguments.Resource} of the export job {item.Job.Id} is no longer exportable.");
            if (!target.ExportTarget.Child)
            {
                // A child surface's read composes its owner's, which its row source checks as it reads.
                _ = await access.RequireAsync(arguments.Resource, AccessActions.Read, cancellationToken).ConfigureAwait(false);
            }

            // --- 2. The workbook, under the background cap, reporting after each page of the parent sheet ---
            ExportRun run = new(context, (written, total) => item.Progress.Report(
                ExcelJobs.Percent(written, total),
                new JobMessage(ExcelJobs.ExportProgress, new Dictionary<string, object?>(StringComparer.Ordinal) { [ExcelJobs.RowsArgument] = written }),
                state: null));
            (ExportOutcome outcome, string requestJson) = arguments switch
            {
                DisplayExportJobArguments display => (await exports.DisplayAsync(target.ExportTarget, display.Request, run, cancellationToken).ConfigureAwait(false), Json(display.Request)),
                ForImportExportJobArguments editable => (await exports.EditableAsync(target.ExportTarget, editable.Request, run, cancellationToken).ConfigureAwait(false), Json(editable.Request)),
                _ => throw new InvalidOperationException($"The export job {item.Job.Id} carries a {arguments.Kind} export, which this handler does not run."),
            };

            await using (outcome.ConfigureAwait(false))
            {
                // --- 3. The file, staged as the requester's; confirmed only when the row commits ---
                BlobDescriptor staged = await blobs.StageAsync(
                    new BlobStageRequest(Export.FileKind, outcome.Workbook, outcome.Workbook.Length, BlobContentTypes.Xlsx, outcome.FileName),
                    cancellationToken).ConfigureAwait(false);

                // --- 4. The row that owns the file, on the job's completion batch ---
                Export row = new()
                {
                    Resource = arguments.Resource,
                    Kind = arguments.Kind,
                    RequestJson = requestJson,
                    FileId = staged.Id,
                    FileName = outcome.FileName,
                    RowCount = outcome.Rows,
                    ExpiresAt = accessor.Current.Now.AddDays(_options.ExportFileRetentionDays),
                    JobId = item.Job.Id,
                };
                EnlistedSave<Export> saved = await this.EnlistSaveAsync(host, [row], new EnlistSaveOptions { ServerOwned = ServerOwned }).ConfigureAwait(false);
                _ = await host.PersistAsync(CancellationToken.None).ConfigureAwait(false);

                // --- 5. The notice, sent once the row commits, then the outcome ---
                if (item.Job.RequestedById is int requester)
                {
                    item.NotifyOnSuccess(new NotificationRequest(
                        ExcelJobs.ExportReadyType,
                        [requester],
                        new ExportReadyArguments(outcome.FileName, outcome.Rows, staged.Id),
                        CoreResources.Export,
                        saved.Rows[0].Id,
                        ActorUserId: null,
                        DedupKey: null));
                }

                item.Succeed();
            }
        }

        /// <summary>The request as the Export row stores it.</summary>
        /// <typeparam name="TRequest">The request's type.</typeparam>
        /// <param name="request">The request.</param>
        /// <returns>The JSON.</returns>
        private static string Json<TRequest>(TRequest request)
        {
            return JsonSerializer.Serialize(request, TellmaJson.Options);
        }
    }
}
