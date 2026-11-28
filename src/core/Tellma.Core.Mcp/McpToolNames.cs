// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Mcp
{
    /// <summary>
    ///     The names of the tenant MCP server's tools: the platform's eight, and those reserved for
    ///     the platform's later tools. Tool and argument names are a compatibility surface: adding is
    ///     safe, renaming or removing is a break. Every name under <see cref="Prefix" /> is the
    ///     platform's, so no distribution tool may begin with it.
    /// </summary>
    public static class McpToolNames
    {
        /// <summary>The prefix every platform tool name begins with.</summary>
        public const string Prefix = "tellma_";

        /// <summary>Who the caller is, in which tenant, and what they may do there.</summary>
        public const string Whoami = "tellma_whoami";

        /// <summary>The entity catalogue, or one entity's description.</summary>
        public const string Describe = "tellma_describe";

        /// <summary>The rows of one entity.</summary>
        public const string Query = "tellma_query";

        /// <summary>Grouped totals over one entity.</summary>
        public const string Aggregate = "tellma_aggregate";

        /// <summary>Whole records of one entity by id.</summary>
        public const string Get = "tellma_get";

        /// <summary>Creates or updates records of one entity.</summary>
        public const string Save = "tellma_save";

        /// <summary>Deletes records of one entity.</summary>
        public const string Delete = "tellma_delete";

        /// <summary>Runs an action on records of one entity.</summary>
        public const string Action = "tellma_action";

        /// <summary>Reserved for a later export tool.</summary>
        public const string ReservedExport = "tellma_export";

        /// <summary>Reserved for a later import tool.</summary>
        public const string ReservedImport = "tellma_import";

        /// <summary>Reserved for a later long-running job tool.</summary>
        public const string ReservedJob = "tellma_job";

        /// <summary>Reserved for a later notifications tool.</summary>
        public const string ReservedNotifications = "tellma_notifications";

        /// <summary>Reserved for a later upload tool.</summary>
        public const string ReservedUpload = "tellma_upload";

        /// <summary>Reserved for a later access-check tool.</summary>
        public const string ReservedCheckAccess = "tellma_check_access";

        /// <summary>Reserved for a later report tool.</summary>
        public const string ReservedReport = "tellma_report";

        /// <summary>Every name this release ships or reserves, in declaration order.</summary>
        public static IReadOnlyList<string> All { get; } =
        [
            Whoami, Describe, Query, Aggregate, Get, Save, Delete, Action,
            ReservedExport, ReservedImport, ReservedJob, ReservedNotifications, ReservedUpload, ReservedCheckAccess, ReservedReport,
        ];

        /// <summary>Whether a tool name is the platform's: it begins with <see cref="Prefix" />, whatever its case.</summary>
        /// <param name="name">The tool's name.</param>
        /// <returns>True when only the platform may use the name.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name" /> is null.</exception>
        public static bool IsPlatformName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
