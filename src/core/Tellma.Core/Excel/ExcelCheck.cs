// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Blobs;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Two checks of the Excel codec. <c>excel</c> reads the model: a warning, never a problem, for
    ///     each property excluded from Excel that the server owns, whose exclusion is redundant.
    ///     <c>excel.deployment</c> reads the host: the scratch directory every workbook spools to
    ///     exists and takes a scratch file.
    /// </summary>
    /// <remarks>
    ///     A build step without a deployment runs <c>excel</c> and skips <c>excel.deployment</c>: the
    ///     scratch directory is the deployment's.
    /// </remarks>
    /// <param name="deployment">True for <c>excel.deployment</c>; false for <c>excel</c>.</param>
    internal sealed class ExcelCheck(bool deployment) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => deployment ? CoreCheckNames.ExcelDeployment : CoreCheckNames.Excel;

        /// <inheritdoc />
        public bool RequiresInfrastructure => deployment;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            IReadOnlyList<CompositionProblem> problems = deployment
                ? CheckScratch(services.GetRequiredService<ScratchDirectory>())
                : WarnRedundantExclusions(services, cancellationToken);
            return Task.FromResult(problems);
        }

        /// <summary>Logs a warning for each server-owned property excluded from Excel.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="cancellationToken">Stops the walk between entities.</param>
        /// <returns>No problem: a redundant exclusion never keeps the host from starting.</returns>
        private static IReadOnlyList<CompositionProblem> WarnRedundantExclusions(IServiceProvider services, CancellationToken cancellationToken)
        {
            ILogger logger = services.GetService<ILoggerFactory>()?.CreateLogger(ExcelLog.Category) ?? NullLogger.Instance;

            // A model with a problem has no metadata; the entity-metadata check reports it.
            EntityMetadataProvider? metadata = services.GetRequiredService<EntityMetadataSource>().Build.Provider;
            foreach (EntityMetadata entity in metadata?.All ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (PropertyMetadata property in entity.Properties)
                {
                    if (property.ExcludedFromExcel && property.Ownership is PropertyOwnership.ServerOwned or PropertyOwnership.Derived or PropertyOwnership.DatabaseOwned)
                    {
                        ExcelLog.RedundantExclusion(logger, entity.Name, property.Name, property.Ownership.ToString());
                    }
                }
            }

            return [];
        }

        /// <summary>The scratch directory exists and takes a scratch file, which is created and discarded.</summary>
        /// <param name="scratch">The scratch directory.</param>
        /// <returns>The problems.</returns>
        private IReadOnlyList<CompositionProblem> CheckScratch(ScratchDirectory scratch)
        {
            // --- The directory must exist: the exports never create it ---
            string fix = $"Create the directory and grant the host's account write access to it, or set '{ScratchDirectory.ConfigurationKey}' to a directory that is.";
            if (!Directory.Exists(scratch.Path))
            {
                return [new CompositionProblem(Name, $"The scratch directory '{scratch.Path}' does not exist.", fix)];
            }

            // --- A scratch file, created exactly as an export creates one, then discarded ---
            try
            {
                using FileStream probe = ScratchFiles.Create(scratch.Path);
                probe.WriteByte(0);
                return [];
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return [new CompositionProblem(Name, $"The scratch directory '{scratch.Path}' does not take a file: {failure.Message}", fix)];
            }
        }
    }
}
