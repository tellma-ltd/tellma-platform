// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Reports
{
    /// <summary>The codes a report's save or delete is refused with, each a resource key the client renders with the error's arguments.</summary>
    public static class ReportValidationCodes
    {
        /// <summary>
        ///     The definition document holds a member its shape does not declare, or a value its member
        ///     cannot hold. Argument: <c>pointer</c>, the JSON pointer of the member.
        /// </summary>
        public const string UnknownMember = "Report.UnknownMember";

        /// <summary>
        ///     A code that starts with a composed feature's name and a dot is reserved to that feature's
        ///     standard reports. Argument: <c>prefix</c>, the reserved prefix.
        /// </summary>
        public const string CodeReserved = "Report.CodeReserved";

        /// <summary>
        ///     A dashboard's widget shows the report, so it cannot be deleted. Arguments:
        ///     <c>dashboards</c>, the titles of up to five such dashboards the caller can read, and
        ///     <c>others</c>, the count of the rest.
        /// </summary>
        public const string InUse = "Report.InUse";
    }
}
