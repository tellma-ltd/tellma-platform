// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Localization;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §2.3: a job's display text is its reader's, rendered in the reader's language in
    // this order: the job's own label with its arguments; else the handler's label resource,
    // JobHandler_<key with every '.' replaced by '_'>, when a shared resource declares it; else the
    // handler key itself. An argument named entity that names an entity renders as that entity's
    // plural label; every other argument is plain ICU. The browser application renders the same
    // order from its string pack and the jobHandlers section (spec 0028 §11.2).
    /// <summary>Renders a job's label in the reader's language, calendar and display zone.</summary>
    /// <param name="texts">The composed features' shared resources, by key.</param>
    /// <param name="labels">The labels by convention, which an <c>entity</c> argument renders through.</param>
    /// <param name="metadata">The model's entity metadata, which says whether an <c>entity</c> argument names an entity.</param>
    internal sealed class JobLabels(ResourceTexts texts, ILabelProvider labels, IEntityMetadataProvider metadata)
    {
        /// <summary>The argument whose entity name renders as the entity's plural label.</summary>
        public const string EntityArgument = "entity";

        /// <summary>Renders a job's label.</summary>
        /// <param name="job">The job, of which the handler key and the label with its arguments are read.</param>
        /// <param name="localization">The reader's localization.</param>
        /// <returns>The text: the job's label, else the handler's, else the handler key.</returns>
        public string Render(Job job, LocalizationContext localization)
        {
            ArgumentNullException.ThrowIfNull(job);
            return Render(job.HandlerKey, job.LabelKey, job.LabelArgumentsJson, localization);
        }

        /// <summary>Renders a job's label from its parts.</summary>
        /// <param name="handlerKey">The job's handler key.</param>
        /// <param name="labelKey">The job's own label key, set at enqueue; null for the handler's label.</param>
        /// <param name="labelArgumentsJson">The label's arguments as a JSON object; null for none.</param>
        /// <param name="localization">The reader's localization.</param>
        /// <returns>The text: the job's label, else the handler's, else the handler key.</returns>
        public string Render(string handlerKey, string? labelKey, string? labelArgumentsJson, LocalizationContext localization)
        {
            ArgumentNullException.ThrowIfNull(handlerKey);
            ArgumentNullException.ThrowIfNull(localization);

            using IDisposable scope = LocalizationScope.Enter(localization);

            // --- 1. The job's own label, with its arguments ---
            if (labelKey is not null && Format(JobLogText.ResourceName(labelKey), Arguments(labelArgumentsJson, localization)) is string own)
            {
                return own;
            }

            // --- 2. The handler's label, when a shared resource declares it; 3. the handler key ---
            string handlerLabel = JobHandlerKeys.LabelKeyOf(handlerKey);
            return texts.Declares(handlerLabel) && Format(handlerLabel, arguments: null) is string handler ? handler : handlerKey;
        }

        /// <summary>Renders a declared key with its arguments; null when no resource declares it or the message cannot be formatted with them.</summary>
        /// <param name="key">The resource key.</param>
        /// <param name="arguments">The arguments; null for none.</param>
        /// <returns>The text, or null.</returns>
        private string? Format(string key, IReadOnlyDictionary<string, object?>? arguments)
        {
            // A label never fails the reader's page: a message its arguments cannot format falls to the next step.
            try
            {
                return texts.Render(key, arguments);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>The label's arguments, its <c>entity</c> argument rendered as the named entity's plural label.</summary>
        /// <param name="argumentsJson">The arguments as JSON; null for none.</param>
        /// <param name="localization">The reader's localization, whose content slot a localized text renders in.</param>
        /// <returns>The arguments; null for none, or for a text that is not one JSON object.</returns>
        private Dictionary<string, object?>? Arguments(string? argumentsJson, LocalizationContext localization)
        {
            if (argumentsJson is null)
            {
                return null;
            }

            Dictionary<string, object?>? arguments = IcuJsonArguments.Read(argumentsJson, localization.ContentLanguageIndex, out JsonException? _);
            if (arguments is not null
                && arguments.TryGetValue(EntityArgument, out object? value)
                && value is string name
                && metadata.All.FirstOrDefault(entity => string.Equals(entity.Name, name, StringComparison.Ordinal)) is EntityMetadata entity)
            {
                arguments[EntityArgument] = labels.EntityLabel(entity.ClrType, plural: true);
            }

            return arguments;
        }
    }
}
