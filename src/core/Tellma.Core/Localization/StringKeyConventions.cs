// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Core.Localization
{
    // The key grammar is spec 0013 §10.1's.
    /// <summary>
    ///     The resource-key grammar of the shared string resources: how a label key is spelled from
    ///     an entity's name, a member's name or an action's name. The label provider, the client
    ///     metadata's contributors and the conventional key list all spell keys through it, so a key
    ///     has one spelling.
    /// </summary>
    internal static class StringKeyConventions
    {
        /// <summary>The suffix of an entity's plural label key.</summary>
        public const string PluralSuffix = "_Plural";

        /// <summary>The suffix of an entity's create label key.</summary>
        public const string NewSuffix = "_New";

        /// <summary>The infix between an entity's prefix and an action's segment.</summary>
        public const string ActionInfix = "_Action_";

        /// <summary>The prefix of a shared action label key.</summary>
        public const string ActionPrefix = "Action_";

        /// <summary>The shared fallback of every entity's create label.</summary>
        public const string EntityNew = "Entity_New";

        /// <summary>
        ///     The bare member keys Core ships as the shared fallback of every entity's member of that
        ///     name (<c>Name</c>, <c>Parent</c>, …), whichever entities a composition holds: a pack's
        ///     tree entity finds <c>Parent</c> though Core declares no tree entity.
        /// </summary>
        public static IReadOnlyList<string> SharedMemberLabels { get; } =
            ["Name", "Code", "Description", "IsActive", "CreatedAt", "CreatedBy", "ModifiedAt", "ModifiedBy", "Parent"];

        /// <summary>
        ///     The prefix every key of an entity or resource starts with: its qualified name with the
        ///     schema, its first segment, capitalized and its dots written <c>_</c> (<c>gl.Center</c>
        ///     is <c>Gl_Center</c>).
        /// </summary>
        /// <param name="name">The entity's qualified name, or a securable resource.</param>
        /// <returns>The prefix.</returns>
        public static string KeyPrefix(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            string underscored = name.Replace('.', '_');
            return underscored.Length == 0 ? underscored : char.ToUpperInvariant(underscored[0]) + underscored[1..];
        }

        /// <summary>A qualified name's schema, its first segment, capitalized (<c>gl.Center</c> is <c>Gl</c>).</summary>
        /// <param name="name">The qualified name.</param>
        /// <returns>The schema's key spelling; the whole name, capitalized, when it has no dot.</returns>
        public static string SchemaPrefix(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            int dot = name.IndexOf('.', StringComparison.Ordinal);
            return KeyPrefix(dot < 0 ? name : name[..dot]);
        }

        /// <summary>A dotted name's last segment (<c>gl.Center</c> is <c>Center</c>).</summary>
        /// <param name="name">The name.</param>
        /// <returns>The text after its last dot.</returns>
        public static string LastSegment(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name[(name.LastIndexOf('.') + 1)..];
        }

        /// <summary>
        ///     An action's key segment: its name PascalCased, each word after a <c>-</c>, <c>/</c>,
        ///     <c>_</c>, <c>.</c> or space capitalized and the separators removed (<c>take-over</c> is
        ///     <c>TakeOver</c>, <c>preferences/set</c> is <c>PreferencesSet</c>, <c>Activate</c> stays).
        /// </summary>
        /// <param name="action">The action's name.</param>
        /// <returns>The segment.</returns>
        public static string ActionSegment(string action)
        {
            ArgumentNullException.ThrowIfNull(action);

            // Each separator ends a word, and the next letter starts one; a word's other letters
            // keep their case, so a name already in PascalCase is its own segment.
            StringBuilder segment = new(action.Length);
            bool startOfWord = true;
            foreach (char c in action)
            {
                if (c is '-' or '/' or '_' or '.' or ' ')
                {
                    startOfWord = true;
                    continue;
                }

                _ = segment.Append(startOfWord ? char.ToUpperInvariant(c) : c);
                startOfWord = false;
            }

            return segment.ToString();
        }

        /// <summary>A key built from a dotted key, its dots written <c>_</c> and its hyphens kept (<c>core.job.pending-confirmation</c> is <c>core_job_pending-confirmation</c>).</summary>
        /// <param name="prefix">The family's prefix, such as <c>NotificationType_</c>.</param>
        /// <param name="key">The dotted key.</param>
        /// <returns>The resource key.</returns>
        public static string FromDottedKey(string prefix, string key)
        {
            ArgumentNullException.ThrowIfNull(prefix);
            ArgumentNullException.ThrowIfNull(key);
            return prefix + key.Replace('.', '_');
        }
    }
}
