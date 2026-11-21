// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Localization;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §3.7: one item per registered handler, in the registry's key order. The label key is the
    // handler's label resource, which a client renders from its string pack; the key itself is sent
    // as the fallback only when no shared resource declares that resource, so the client never shows
    // a raw resource key (§2.3's order). The schedules page's handler list, the job labels and the
    // stop confirmation read the section.
    /// <summary>
    ///     Writes the <c>jobHandlers</c> section of the client metadata's Core chunk: every handler
    ///     this instance registers, with whether a user schedule may name it, whether a run commits
    ///     in steps, its label and the row a succeeded job leaves.
    /// </summary>
    /// <param name="registry">The handlers this instance registers.</param>
    /// <param name="keys">The keys the composed features' neutral resources declare.</param>
    internal sealed class JobHandlersClientAssetContributor(JobHandlerRegistry registry, INeutralStringKeys keys) : IClientAssetContributor
    {
        /// <summary>The section listing every registered handler.</summary>
        public const string Section = "jobHandlers";

        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- One item per handler, under the platform's JSON rules: camelCase, nulls omitted ---
            ClientJobHandler[] handlers = [.. registry.Handlers.Select(Describe)];
            context.Section(ClientMetadataChunk.Core, Section, writer => JsonSerializer.Serialize(writer, handlers, TellmaJson.Options));
        }

        /// <summary>One handler as the client sees it.</summary>
        /// <param name="handler">The handler's registration.</param>
        /// <returns>The item.</returns>
        private ClientJobHandler Describe(JobHandlerRegistration handler)
        {
            JobHandlerAttribute options = handler.Options;
            string labelKey = JobHandlerKeys.LabelKeyOf(handler.Key);

            // The realizer refuses one of the two set without the other, so they come together or not at all.
            ClientJobResult? result = options.ResultEntity is string entity && options.ResultVia is string via
                ? new ClientJobResult(entity, via)
                : null;
            return new ClientJobHandler(
                handler.Key,
                options.Schedulable,
                options.CommitsInSteps,
                labelKey,
                keys.Declares(labelKey) ? null : handler.Key,
                result);
        }
    }
}
