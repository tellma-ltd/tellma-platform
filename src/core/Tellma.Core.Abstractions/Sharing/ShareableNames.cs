// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>The names every shareable stack shares: its actions, its details extra, its named filter, its access reason and its limits.</summary>
    public static class ShareableNames
    {
        /// <summary>The action that grants users, under the securable action <c>Share</c>.</summary>
        public const string ShareAction = "share";

        /// <summary>The action that grants roles and everyone, under the securable action <c>Publish</c>.</summary>
        public const string PublishAction = "publish";

        /// <summary>The action that changes a row's owner, under the securable action <c>Transfer</c>.</summary>
        public const string TransferAction = "transfer";

        /// <summary>The details extra that lists a row's grants.</summary>
        public const string SharesExtra = "shares";

        /// <summary>The named filter of the rows shared with the caller and owned by someone else, or by no one.</summary>
        public const string SharedWithMeFilter = "shared-with-me";

        /// <summary>The reason the bespoke access criteria answer a "why" with.</summary>
        public const string Reason = "share";

        /// <summary>The most user grants one row holds.</summary>
        public const int MaxUserGrants = 200;

        /// <summary>The most role grants one row holds.</summary>
        public const int MaxRoleGrants = 50;
    }
}
