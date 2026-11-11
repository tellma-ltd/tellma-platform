// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     Builds server-made links from <c>Tellma:PublicOrigin</c> and the client's route grammar:
    ///     <c>/{tenantId}</c> for a tenant's home and <c>/{tenantId}/{segment}/{id}</c> for a record's
    ///     details page.
    /// </summary>
    /// <param name="record">The realised composition, whose configuration holds the public origin.</param>
    /// <param name="stacks">Answers which resources name a stack, and its route segment and operations.</param>
    internal sealed class TellmaLinks(TellmaCompositionRecord record, IStackRouteSource stacks) : ITellmaLinks
    {
        /// <inheritdoc />
        public string Tenant(int tenantId)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(tenantId, 1);

            // Invariant, so the id is written in ASCII digits whatever the caller's culture.
            return string.Create(CultureInfo.InvariantCulture, $"{PublicOrigin()}/{tenantId}");
        }

        /// <inheritdoc />
        public string Target(int tenantId, string? resource, long? id)
        {
            string home = Tenant(tenantId);

            // --- No record to point at: the tenant's home ---
            if (resource is null || id is not { } recordId || recordId < 1)
            {
                return home;
            }

            // --- A record only a stack with a details page can show (spec 0011 §5.10) ---
            if (stacks.Find(resource) is not { } stack || !stack.Operations.HasFlag(StackOperations.Details))
            {
                return home;
            }

            // A segment is kebab-case ASCII; escaping it keeps the link well-formed whatever a
            // distribution's override put there.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{home}/{Uri.EscapeDataString(stack.ResourceSegment)}/{recordId}");
        }

        /// <summary>The configured public origin, which the startup gate has checked carries no trailing slash.</summary>
        /// <returns>The origin.</returns>
        /// <exception cref="InvalidOperationException">The origin is not configured.</exception>
        private string PublicOrigin()
        {
            string? origin = record.Configuration[PublicOriginCheck.Key];
            return string.IsNullOrWhiteSpace(origin)
                ? throw new InvalidOperationException(
                    $"{PublicOriginCheck.Key} is not set, so no link can be built. Set it to the origin users reach the distribution at.")
                : origin;
        }
    }
}
