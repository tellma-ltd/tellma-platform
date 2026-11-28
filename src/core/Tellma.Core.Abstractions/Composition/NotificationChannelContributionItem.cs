// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A notification channel a feature registers, together with the
    ///     <see cref="INotificationChannel" /> sender it registers as a service. The platform composes it
    ///     into the notification channel registry in realisation order, and the startup gate validates
    ///     it.
    /// </summary>
    /// <param name="Descriptor">The channel.</param>
    public sealed record NotificationChannelContributionItem(NotificationChannelDescriptor Descriptor) : FeatureContributionItem;
}
