// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Composition;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     Realises a <see cref="RetiredJobKeyContributionItem" />: holds the key to the grammar and the
    ///     platform's prefix to the platform, and records it.
    /// </summary>
    /// <remarks>
    ///     A retired key some handler still registers, or a built-in schedule still names, is reported
    ///     once every item is realised, by <see cref="JobContributionChecks" />.
    /// </remarks>
    internal sealed class RetiredJobKeyContributionRealizer : IContributionRealizer<RetiredJobKeyContributionItem>
    {
        /// <inheritdoc />
        public void Realize(RetiredJobKeyContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(context);

            // --- The grammar ---
            if (!JobHandlerKeys.IsValid(item.Key))
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' retires the job handler key '{item.Key}', which is not two or more dot-separated segments of lowercase letters, digits and hyphens, each starting with a letter, of at most {JobLimits.MaxKeyLength} characters.",
                    "Retire the key exactly as the removed handler's [JobHandler] attribute declared it."));
                return;
            }

            // --- The platform's prefix belongs to the platform ---
            if (JobHandlerKeys.IsPlatformKey(item.Key) && owner.Name != CoreFeature.FeatureName)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' retires the job handler key '{item.Key}', but the prefix '{JobHandlerKeys.PlatformPrefix}' is reserved for the platform, which alone retires its keys.",
                    "Retire only the keys of the handlers the feature itself removed."));
                return;
            }

            // A key retired twice is retired once.
            if (!context.Record.RetiredJobKeys.Exists(existing => string.Equals(existing.Key, item.Key, StringComparison.Ordinal)))
            {
                context.Record.RetiredJobKeys.Add(new ContributedRetiredJobKey(item.Key, owner));
            }
        }
    }
}
