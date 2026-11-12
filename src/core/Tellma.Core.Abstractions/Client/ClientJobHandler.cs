// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     One registered job handler as the client sees it: an item of the Core chunk's
    ///     <c>jobHandlers</c> section, serialised under the platform's JSON rules (camelCase members,
    ///     null members omitted). The schedules page's handler list, the job labels and the stop
    ///     confirmation read it.
    /// </summary>
    /// <param name="Key">The handler key.</param>
    /// <param name="Schedulable">Whether a user-created schedule may name the key.</param>
    /// <param name="CommitsInSteps">Whether a run commits in several transactions, so the client asks before stopping it.</param>
    /// <param name="LabelKey">The resource key of the handler's label, <see cref="JobHandlerKeys.LabelKeyOf(string)" /> of the key.</param>
    /// <param name="LabelFallback">The text a client shows when no string resource holds <paramref name="LabelKey" />: the key itself; null, and omitted, when one does.</param>
    /// <param name="Result">The row a succeeded job leaves for the user; null, and omitted, when the handler leaves none.</param>
    public sealed record ClientJobHandler(
        string Key,
        bool Schedulable,
        bool CommitsInSteps,
        string LabelKey,
        string? LabelFallback,
        ClientJobResult? Result);
}
