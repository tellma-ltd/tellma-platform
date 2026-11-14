// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Caching;
using Tellma.Core.Queryex.Pipeline;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>
    ///     Looks at what makes two cache keys the same key, which is otherwise not visible from
    ///     outside.
    /// </summary>
    /// <remarks>
    ///     A key that omits something the binding depended on is not a performance problem: it
    ///     hands one request the answer computed for a different one. Where that difference cannot
    ///     yet be produced through the public surface — a Queryex version this engine does not
    ///     implement, an execution fact no fixture sets — the key is asserted here instead, so the
    ///     omission is caught by a test rather than by whatever runs first in production.
    /// </remarks>
    public static class ProbeCacheKeys
    {
        /// <summary>
        ///     Whether two bind keys differing only in Queryex version are told apart.
        /// </summary>
        /// <param name="first">One Queryex version.</param>
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
        public static bool DistinguishesQueryexVersions(
            int first,
            int second,
            QueryexSchema schema,
            EntityDescriptor root)
        {
            BindKey one = Key(first, schema, root, [], negated: false);
            BindKey other = Key(second, schema, root, [], negated: false);

            // Both directions and the hash, because a cache consults the hash to find the bucket
            // and equality only to pick within it; a key that hashed alike and compared alike is
            // one key however the two were built.
            return Apart(one, other);
        }

        /// <summary>Whether two compilations of whole queries share one cache entry.</summary>
        /// <param name="first">One query.</param>
        /// <param name="second">The other.</param>
        /// <param name="options">The options both compile under.</param>
        /// <returns>True when a compilation of one would be served for the other.</returns>
        /// <remarks>
        ///     Asked of the key itself, both directions and the hash, so an answer of "shared" cannot
        ///     be an accident of which bucket two keys happened to land in.
        /// </remarks>
        public static bool ShareQueryEntry(QuerySpec first, QuerySpec second, QueryCompilationOptions options)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            ArgumentNullException.ThrowIfNull(options);

            return ShareQueryEntry(first, options, second, options);
        }

        /// <summary>Whether two compilations of whole queries share one cache entry.</summary>
        /// <param name="first">One query.</param>
        /// <param name="firstOptions">The options it compiles under.</param>
        /// <param name="second">The other query.</param>
        /// <param name="secondOptions">The options that one compiles under.</param>
        /// <returns>True when a compilation of one would be served for the other.</returns>
        /// <remarks>
        ///     The form that can vary the options, for what a compilation depends on that the query
        ///     itself does not carry — which parameters it leaves out, above all.
        /// </remarks>
        public static bool ShareQueryEntry(
            QuerySpec first,
            QueryCompilationOptions firstOptions,
            QuerySpec second,
            QueryCompilationOptions secondOptions)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(firstOptions);
            ArgumentNullException.ThrowIfNull(second);
            ArgumentNullException.ThrowIfNull(secondOptions);

            var one = QueryKey.Of(
                first,
                firstOptions,
                OmittedParameters.Resolve(firstOptions, nameof(firstOptions)));

            var other = QueryKey.Of(
                second,
                secondOptions,
                OmittedParameters.Resolve(secondOptions, nameof(secondOptions)));

            return Same(one, other);
        }

        /// <summary>Whether two constant statements share one cache entry.</summary>
        /// <param name="first">One statement's items.</param>
        /// <param name="firstOptions">The options it compiles under.</param>
        /// <param name="second">The other statement's items.</param>
        /// <param name="secondOptions">The options that one compiles under.</param>
        /// <returns>True when a compilation of one would be served for the other.</returns>
        public static bool ShareConstantEntry(
            IReadOnlyList<ConstantItem> first,
            ConstantCompilationOptions firstOptions,
            IReadOnlyList<ConstantItem> second,
            ConstantCompilationOptions secondOptions)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(firstOptions);
            ArgumentNullException.ThrowIfNull(second);
            ArgumentNullException.ThrowIfNull(secondOptions);

            return Same(
                QueryKey.OfConstant([.. first], firstOptions),
                QueryKey.OfConstant([.. second], secondOptions));
        }

        /// <summary>Whether a constant statement and a whole query could share one cache entry.</summary>
        /// <param name="items">The statement's items.</param>
        /// <param name="constantOptions">The options the statement compiles under.</param>
        /// <param name="spec">The query.</param>
        /// <param name="queryOptions">The options the query compiles under.</param>
        /// <returns>True when a compilation of one would be served for the other.</returns>
        /// <remarks>
        ///     The two kinds are remembered side by side, so the keys themselves have to keep them
        ///     apart whatever the texts say.
        /// </remarks>
        public static bool ShareEntryAcrossKinds(
            IReadOnlyList<ConstantItem> items,
            ConstantCompilationOptions constantOptions,
            QuerySpec spec,
            QueryCompilationOptions queryOptions)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(constantOptions);
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(queryOptions);

            return Same(
                QueryKey.OfConstant([.. items], constantOptions),
                QueryKey.Of(spec, queryOptions, OmittedParameters.Resolve(queryOptions, nameof(queryOptions))));
        }

        /// <summary>
        ///     Whether the bindings of one text are told apart by what was folded out of it.
        /// </summary>
        /// <param name="schema">The schema the text binds against.</param>
        /// <param name="root">The entity it resolves paths from.</param>
        /// <returns>
        ///     True when a binding with a parameter folded out, the same under a negation, and one
        ///     with nothing folded are three distinct keys.
        /// </returns>
        /// <remarks>
        ///     Asked of the key because the three bindings are of one text under one context: only
        ///     the key can say they are different trees.
        /// </remarks>
        public static bool DistinguishesFolding(QueryexSchema schema, EntityDescriptor root)
        {
            ParameterSymbol omitted = new("Customer", BoundType.Numeric, QueryexNullity.Nullable);
            BindKey whole = Key(QueryexLanguage.Version, schema, root, [], negated: false);
            BindKey folded = Key(QueryexLanguage.Version, schema, root, [omitted], negated: false);
            BindKey denied = Key(QueryexLanguage.Version, schema, root, [omitted], negated: true);

            return Apart(whole, folded) && Apart(folded, denied) && Apart(whole, denied);
        }

        /// <summary>Whether two keys are one key: equal both ways and hashing alike.</summary>
        /// <param name="one">One key.</param>
        /// <param name="other">The other.</param>
        /// <returns>True when they are.</returns>
        private static bool Same(QueryKey one, QueryKey other)
        {
            return one.Equals(other) && other.Equals(one) && one.GetHashCode() == other.GetHashCode();
        }

        /// <summary>Whether two bind keys are distinct: unequal both ways and hashing apart.</summary>
        /// <param name="one">One key.</param>
        /// <param name="other">The other.</param>
        /// <returns>True when they are.</returns>
        private static bool Apart(BindKey one, BindKey other)
        {
            return !one.Equals(other) && !other.Equals(one) && one.GetHashCode() != other.GetHashCode();
        }

        /// <summary>
        ///     Builds a bind key that differs from its siblings only in what is passed here.
        /// </summary>
        /// <param name="queryexVersion">The Queryex version.</param>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The root entity.</param>
        /// <param name="omitted">The omitted declarations the text refers to.</param>
        /// <param name="negated">Whether the text was folded under an odd number of negations.</param>
        /// <returns>The key.</returns>
        private static BindKey Key(
            int queryexVersion,
            QueryexSchema schema,
            EntityDescriptor root,
            ImmutableArray<ParameterSymbol> omitted,
            bool negated)
        {
            return new BindKey(
                "Amount + 1",
                schema,
                root,
                QueryexMode.Value,
                queryexVersion,
                hasUser: true,
                hasGroupingKeys: false,
                directions: false,
                QueryexLimits.Default,
                [],
                omitted,
                negated);
        }
    }
}
