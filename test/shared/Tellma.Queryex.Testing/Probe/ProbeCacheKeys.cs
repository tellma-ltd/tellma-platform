// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Caching;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>
    ///     Looks at what makes two cache keys the same key, which is otherwise not visible from
    ///     outside.
    /// </summary>
    /// <remarks>
    ///     A key that omits something the binding depended on is not a performance problem: it
    ///     hands one request the answer computed for a different one. Where that difference cannot
    ///     yet be produced through the public surface — a language version this engine does not
    ///     implement, an execution fact no fixture sets — the key is asserted here instead, so the
    ///     omission is caught by a test rather than by whatever runs first in production.
    /// </remarks>
    public static class ProbeCacheKeys
    {
        /// <summary>
        ///     Whether two bind keys differing only in language version are told apart.
        /// </summary>
        /// <param name="first">One language version.</param>
        /// <param name="second">The other.</param>
        /// <param name="schema">The schema both bound against.</param>
        /// <param name="root">The entity both resolved paths from.</param>
        /// <returns>True when the two keys are distinct.</returns>
        /// <remarks>
        ///     Reachable only from here while one version exists, because the options records
        ///     refuse any version this engine does not implement. It is pinned now rather than when
        ///     a second version arrives: at that point the two versions bind the same text to
        ///     different trees, and a key that had never distinguished them would serve one
        ///     version's tree for the other's request.
        /// </remarks>
        public static bool DistinguishesLanguageVersions(
            int first,
            int second,
            QueryexSchema schema,
            EntityDescriptor root)
        {
            BindKey one = Key(first, schema, root);
            BindKey other = Key(second, schema, root);

            // Both directions and the hash, because a cache consults the hash to find the bucket
            // and equality only to pick within it; a key that hashed alike and compared alike is
            // one key however the two were built.
            return !one.Equals(other)
                && !other.Equals(one)
                && one.GetHashCode() != other.GetHashCode();
        }

        /// <summary>Builds a bind key that differs from its siblings only in language version.</summary>
        /// <param name="languageVersion">The language version.</param>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The root entity.</param>
        /// <returns>The key.</returns>
        private static BindKey Key(int languageVersion, QueryexSchema schema, EntityDescriptor root)
        {
            return new BindKey(
                "Amount + 1",
                schema,
                root,
                QueryexMode.Value,
                languageVersion,
                hasUser: true,
                hasGroupingKeys: false,
                directions: false,
                QueryexLimits.Default,
                []);
        }
    }
}
