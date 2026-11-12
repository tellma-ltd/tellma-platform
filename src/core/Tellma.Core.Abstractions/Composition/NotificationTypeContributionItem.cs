// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A notification type a feature registers, declared once by the feature whose code raises it.
    ///     The platform composes it into the notification type registry in realisation order, and the
    ///     startup gate validates it.
    /// </summary>
    /// <param name="Descriptor">The type.</param>
    public sealed record NotificationTypeContributionItem(NotificationTypeDescriptor Descriptor) : FeatureContributionItem;
}
