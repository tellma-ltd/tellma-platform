// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Abstractions.Internal
{
    /// <summary>The rule every context that raises notifications shares: it needs the scope's notifier.</summary>
    internal static class ContextNotifier
    {
        /// <summary>Returns the context's notifier, or refuses when the host registers none.</summary>
        /// <param name="notifier">The notifier the pipeline set; null when the host registers none.</param>
        /// <returns>The notifier.</returns>
        /// <exception cref="NotSupportedException"><paramref name="notifier" /> is null.</exception>
        public static INotifier Require(INotifier? notifier)
        {
            return notifier ?? throw new NotSupportedException(
                $"Notifications are not available: the host registers no {nameof(INotifier)} service.");
        }
    }
}
