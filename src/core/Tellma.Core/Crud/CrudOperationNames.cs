// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Crud
{
    // The closed set is spec 0015 §16.1's crud.operation column.
    /// <summary>
    ///     The <c>crud.operation</c> values: the stack operations the pipeline runs, each also the
    ///     suffix of its unit of work's name, <c>&lt;Resource&gt;:&lt;operation&gt;</c>.
    /// </summary>
    internal static class CrudOperationNames
    {
        /// <summary>A page of rows, on a stack or a child query surface.</summary>
        public const string Query = "query";

        /// <summary>One row's details.</summary>
        public const string Get = "get";

        /// <summary>Several rows' details.</summary>
        public const string GetByIds = "get_by_ids";

        /// <summary>Every row of a cached list.</summary>
        public const string All = "all";

        /// <summary>A save.</summary>
        public const string Save = "save";

        /// <summary>A delete by ids.</summary>
        public const string Delete = "delete";

        /// <summary>A delete by query.</summary>
        public const string DeleteByQuery = "delete_by_query";

        /// <summary>A delete of tree nodes with their descendants.</summary>
        public const string DeleteWithDescendants = "delete_with_descendants";

        /// <summary>An entity action, built in or declared.</summary>
        public const string Action = "action";

        /// <summary>The children of tree nodes.</summary>
        public const string GetByParentIds = "get_by_parent_ids";

        /// <summary>The rows of an Excel export.</summary>
        public const string ExcelRows = "excel_rows";
    }
}
