// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.2: an export's /start route enqueues one core.export job with no entity, its row
    // inserted only by the handler once it has a file to own; import/start enlists the insert of
    // its Import row with the invoker's frame and persists it, and the imports' service enqueues the
    // row's job in that persist, so the row, the job and the row's JobId commit together. Each job is
    // requested by and runs as the caller, under the caller's negotiated localization.
    /// <summary>The start of the background exports and imports: the export jobs' enqueue, and the import rows' insert. Scoped.</summary>
    /// <param name="jobs">The job queue.</param>
    /// <param name="accessor">The caller's context.</param>
    /// <param name="host">The write host of the frame the invoker opened around the action method.</param>
    /// <param name="options">The codec's options, whose retention dates an import row.</param>
    internal sealed class ExcelJobStarts(IJobQueue jobs, IRequestContextAccessor accessor, IOpenWriteHost host, IOptions<ExcelOptions> options)
    {
        /// <summary>The codec's options.</summary>
        private readonly ExcelOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

        /// <summary>The caller's negotiated localization, which a job renders in.</summary>
        public ExcelJobCulture Culture => ExcelJobCulture.Of(accessor.Current);

        /// <summary>Enqueues a background export of a resource, under the caller's localization.</summary>
        /// <param name="arguments">The export's arguments, of its shape's case.</param>
        /// <param name="cancellationToken">Cancels the enqueue.</param>
        /// <returns>The job, with no row: the handler inserts it.</returns>
        /// <exception cref="BadRequestException">The arguments take more bytes than a job carries.</exception>
        public async Task<JobAccepted> ExportAsync(ExportJobArguments arguments, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            JsonElement stored = ExcelJobs.Stored(arguments);
            CheckSize(stored.GetRawText());

            int? caller = accessor.Current.UserId;
            JobRequest request = new(
                ExcelJobs.ExportHandlerKey,
                stored,
                DueAt: null,
                RequestedById: caller,
                RunAsUserId: caller,
                Entity: null,
                Label: ExcelJobs.Label(ExcelJobs.ExportLabel, arguments.Resource));
            IReadOnlyList<int> ids = await jobs.EnqueueAsync([request], cancellationToken).ConfigureAwait(false);
            return new JobAccepted(ids[0], ResourceId: null);
        }

        /// <summary>
        ///     Inserts the row of a background import through the frame of the action method that
        ///     starts it, whose persist enqueues the row's job.
        /// </summary>
        /// <param name="writer">The action method's class, the row's enlisted writer.</param>
        /// <param name="resource">The imported stack's resource.</param>
        /// <param name="request">The import's request.</param>
        /// <param name="cancellationToken">Cancels the persist.</param>
        /// <returns>The job and the row.</returns>
        /// <exception cref="BadRequestException">The request, with the caller's localization, takes more bytes than a job carries.</exception>
        /// <exception cref="ValidationException">The upload is not the caller's own staged import file.</exception>
        public async Task<JobAccepted> ImportAsync(IEnlists<Import> writer, string resource, StartImportRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentNullException.ThrowIfNull(request);

            // --- The row: the request it reruns, the upload it owns, and when retention removes it ---
            RequestContext caller = accessor.Current;
            string requestJson = JsonSerializer.Serialize(request, TellmaJson.Options);
            CheckSize(JsonSerializer.Serialize(new ImportJobArguments(resource, Culture, request), TellmaJson.Options));
            Import row = new()
            {
                Resource = resource,
                Mode = request.Mode,
                RequestJson = requestJson,
                FileId = request.FileId,
                ExpiresAt = caller.Now.AddDays(_options.ImportFileRetentionDays),
            };

            // --- The insert with the invoker's frame, whose persist enqueues the job ---
            EnlistedSave<Import> saved = await writer.EnlistSaveAsync(host, [row]).ConfigureAwait(false);
            _ = await host.PersistAsync(cancellationToken).ConfigureAwait(false);
            Import inserted = saved.Rows[0];
            int jobId = inserted.JobId ?? throw new InvalidOperationException($"The import {inserted.Id} committed without its job.");
            return new JobAccepted(jobId, inserted.Id);
        }

        /// <summary>Refuses arguments a job cannot carry, before anything is written.</summary>
        /// <param name="json">The arguments, serialized.</param>
        /// <exception cref="BadRequestException">The arguments take more bytes than a job carries.</exception>
        private static void CheckSize(string json)
        {
            int bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes > JobLimits.MaxHandlerArgumentsBytes)
            {
                throw new BadRequestException(
                    string.Create(CultureInfo.InvariantCulture, $"The request takes {bytes} bytes as JSON; a background export or import carries at most {JobLimits.MaxHandlerArgumentsBytes}. Name fewer ids, or select the rows by the grid's clauses."),
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["bytes"] = bytes,
                        ["maxBytes"] = JobLimits.MaxHandlerArgumentsBytes,
                    });
            }
        }
    }
}
