// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Assignments
{
    /// <summary>One assignment of a row to a user.</summary>
    /// <param name="Resource">The entity name of the assignable stack the row belongs to (<c>acme.Invoice</c>).</param>
    /// <param name="TargetId">The row's id; positive.</param>
    /// <param name="AssigneeId">The user the row is assigned to; positive.</param>
    /// <param name="Comment">The assigner's comment; null for none.</param>
    /// <param name="Caption">How the assignment reads in the assignee's list; null for the default.</param>
    /// <param name="AssignedById">The assigner; null for the scope's user, or the system in a system scope.</param>
    public sealed record AssignmentRequest(
        string Resource,
        long TargetId,
        int AssigneeId,
        string? Comment = null,
        AssignmentCaption? Caption = null,
        int? AssignedById = null);
}
