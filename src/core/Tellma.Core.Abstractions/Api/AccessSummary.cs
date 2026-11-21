// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>What the caller may do, as the <c>me</c> envelope carries it.</summary>
    /// <param name="Tag">The caller's composite permissions tag, in its wire form; the same value as the envelope's <c>permissions</c> tag.</param>
    /// <param name="FormatVersion">The envelope's format version.</param>
    /// <param name="IsSystem">Whether the caller is the platform's own system principal, which every securable admits.</param>
    /// <param name="Securables">Every registered securable the caller holds, each once, in the registry's order.</param>
    /// <param name="Problems">The problems found in the caller's roles' permissions, which an administrator fixes.</param>
    /// <param name="ValidUntil">
    ///     When the summary next changes on its own: the next start or end of one of the caller's
    ///     membership windows; null when none lies ahead.
    /// </param>
    public sealed record AccessSummary(
        string Tag,
        int FormatVersion,
        bool IsSystem,
        IReadOnlyList<SecurableSummary> Securables,
        IReadOnlyList<AccessProblem> Problems,
        DateTimeOffset? ValidUntil);
}
