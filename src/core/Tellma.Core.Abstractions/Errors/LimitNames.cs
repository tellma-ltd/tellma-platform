// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     The values of <see cref="LimitExceededException.Limit" /> for the platform's ceilings. A
    ///     feature's own limit uses its code instead (<c>Excel.Export.RowLimitExceeded</c>).
    /// </summary>
    public static class LimitNames
    {
        /// <summary>A query's page size above the stack's maximum.</summary>
        public const string MaxTake = "MaxTake";

        /// <summary>A query's skip plus page size above the stack's skip window.</summary>
        public const string SkipWindow = "skipWindow";

        /// <summary>More entities in one save than the stack allows.</summary>
        public const string MaxSaveCount = "MaxSaveCount";

        /// <summary>More rows in one save, counted at every depth, than the stack allows.</summary>
        public const string MaxRowsPerSave = "MaxRowsPerSave";

        /// <summary>More rows in one child collection of one parent than the collection allows.</summary>
        public const string MaxChildren = "MaxChildren";

        /// <summary>More ids in one request than the stack allows.</summary>
        public const string MaxIds = "MaxIds";

        /// <summary>More rows matched by a delete by query than the stack allows.</summary>
        public const string MaxDeleteByQueryRows = "MaxDeleteByQueryRows";

        /// <summary>A search text longer than the stack allows.</summary>
        public const string MaxSearchLength = "MaxSearchLength";

        /// <summary>More details extras in one request than the stack allows.</summary>
        public const string MaxExtras = "MaxExtras";

        /// <summary>More entities in one save than the web surface allows.</summary>
        public const string Entities = "entities";

        /// <summary>A request body larger than the endpoint allows.</summary>
        public const string Body = "body";
    }
}
