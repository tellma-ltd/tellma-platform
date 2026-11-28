// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="NotificationTypeContributionItem" />s: each descriptor as a singleton,
    ///     in realisation order, which is the order the notification type registry lists the types
    ///     in. Each is recorded with its feature, so the startup gate can say who contributed a type
    ///     it refuses.
    /// </summary>
    internal sealed class NotificationTypeContributionRealizer : IContributionRealizer<NotificationTypeContributionItem>
    {
        /// <inheritdoc />
        public void Realize(NotificationTypeContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // The sugar refuses a null descriptor; an item built by hand can still carry one.
            if (item.Descriptor is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes a notification type without a descriptor.",
                    "Contribute a notification type with its descriptor: contribution.NotificationType(new NotificationTypeDescriptor(key, category, mutable, targetResource))."));
                return;
            }

            // Everything else about a type (its key, its category, its target) is the startup
            // gate's to judge, against every other type and the model.
            context.Record.NotificationTypes.Add(new ContributedNotificationType(item.Descriptor, owner));
            _ = context.Services.AddSingleton(item.Descriptor);
        }
    }
}
