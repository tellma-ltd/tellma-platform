// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A job handler key a feature retired: no handler registers it any longer, so its queued jobs
    ///     are cancelled and the schedules naming it pause.
    /// </summary>
    /// <param name="Key">The retired handler key.</param>
    public sealed record RetiredJobKeyContributionItem(string Key) : FeatureContributionItem;
}
