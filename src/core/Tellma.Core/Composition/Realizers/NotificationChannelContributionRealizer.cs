// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="NotificationChannelContributionItem" />s: each descriptor as a
    ///     singleton, in realisation order, which is the order the notification channel registry lists
    ///     the channels in. Each is recorded with its feature, so the startup gate can say who
    ///     contributed a channel it refuses.
    /// </summary>
    internal sealed class NotificationChannelContributionRealizer : IContributionRealizer<NotificationChannelContributionItem>
    {
        /// <inheritdoc />
        public void Realize(NotificationChannelContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses a null descriptor; an item built by hand can still carry one.
            if (item.Descriptor is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes a notification channel without a descriptor.",
                    "Contribute a notification channel with its descriptor: contribution.NotificationChannel(new NotificationChannelDescriptor(key, defaultEnabled))."));
                return;
            }

            // The key's grammar, its uniqueness and the channel's sender are the startup gate's to judge.
            context.Record.NotificationChannels.Add(new ContributedNotificationChannel(item.Descriptor, owner));
            _ = context.Services.AddSingleton(item.Descriptor);
        }
    }
}
