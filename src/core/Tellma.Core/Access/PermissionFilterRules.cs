// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The rules are spec 0014 §3.7 step 6, rules 5 and 6: the length before the engine reads the
    // text, then a predicate over the root, with a user and no parameters, within one stored filter's
    // profile. The role save and the editor's discovery both judge a filter here, so they cannot
    // disagree.
    /// <summary>
    ///     Judges one permission filter's text as a role save does: no longer than the configured
    ///     maximum, then a filter over its root that names no parameter and stays within the stored
    ///     filter's ceilings.
    /// </summary>
    internal static class PermissionFilterRules
    {
        /// <summary>Judges a filter.</summary>
        /// <param name="filter">The filter's text.</param>
        /// <param name="root">The entity the filter reads, of <paramref name="schema" />; null when the schema has none by the filter's root name, so nothing validates over it.</param>
        /// <param name="schema">The tenant's current schema.</param>
        /// <param name="stamp">The language version the text is read under.</param>
        /// <param name="maxLength">The longest a filter may be.</param>
        /// <param name="validate">Validates one filter text; the engine's own validation but in tests.</param>
        /// <returns>The verdict.</returns>
        public static PermissionFilterVerdict Check(
            string filter,
            EntityDescriptor? root,
            QueryexSchema schema,
            int stamp,
            int maxLength,
            Func<string, ValidationOptions, QueryexResult<ValidatedExpression>> validate)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(validate);

            // --- Rule 5: the length, before the engine reads the text ---
            if (filter.Length > maxLength)
            {
                return new PermissionFilterVerdict(Valid: false, MaxLengthExceeded: maxLength, Diagnostics: []);
            }

            // --- Rule 6: a predicate over the root, with a user and no parameters, within one stored filter's
            // profile; a schema without the root validates nothing over it, as the drift classifier reads it ---
            if (root is null)
            {
                return new PermissionFilterVerdict(Valid: false, MaxLengthExceeded: null, Diagnostics: []);
            }

            QueryexResult<ValidatedExpression> result = validate(filter, new ValidationOptions
            {
                Schema = schema,
                Root = root,
                Mode = QueryexMode.Filter,
                QueryexVersion = stamp,
                HasUser = true,
                Parameters = [],
                Limits = QueryexProfiles.AccessLimits,
            });
            bool valid = result.Succeeded && result.Diagnostics.Count == 0 && !string.IsNullOrWhiteSpace(filter);
            return new PermissionFilterVerdict(valid, MaxLengthExceeded: null, result.Diagnostics);
        }
    }

    /// <summary>A permission filter's verdict.</summary>
    /// <param name="Valid">Whether the filter may be stored.</param>
    /// <param name="MaxLengthExceeded">The maximum the filter's length exceeds, when it is too long; the engine then never read it.</param>
    /// <param name="Diagnostics">The engine's diagnostics; empty when it found none, or never read the text.</param>
    internal sealed record PermissionFilterVerdict(bool Valid, int? MaxLengthExceeded, IReadOnlyList<QueryexDiagnostic> Diagnostics);
}
