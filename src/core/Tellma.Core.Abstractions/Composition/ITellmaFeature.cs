// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A composable unit of functionality: a pack, a distribution's own feature, or Core itself.
    ///     A feature declares the features it builds on and the options it binds, then contributes
    ///     data-only items that the platform realises in dependency order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A feature never sees the service container or the EF model builder. Everything it adds
    ///         is a <see cref="FeatureContributionItem" /> record, and the platform turns each item
    ///         into registrations. That keeps this contract free of every package reference, so a
    ///         module ships a feature without depending on the runtime.
    ///     </para>
    ///     <para>
    ///         The platform adds its own <c>core</c> feature first and unconditionally. Every other
    ///         feature follows it without declaring it.
    ///     </para>
    /// </remarks>
    public interface ITellmaFeature
    {
        /// <summary>
        ///     The feature's stable, unique identity: lowercase dotted segments such as
        ///     <c>core</c>, <c>gl</c> or <c>acme</c>. It names the feature in diagnostics, in securable
        ///     descriptors and in the provisioning-step and handler-key grammars
        ///     (<c>&lt;name&gt;.&lt;step&gt;</c>). Two features carrying the same name are a
        ///     composition problem.
        /// </summary>
        string Name { get; }

        /// <summary>
        ///     Records the features this one requires and the options it binds. Runs before the
        ///     feature's contribution, and records nothing else.
        /// </summary>
        /// <param name="declaration">The declaration to record into.</param>
        [SuppressMessage(
            "Naming",
            "CA1716:Identifiers should not match keywords",
            Justification = "The contract's established name; features are written in C#, where Declare is not a keyword.")]
        void Declare(FeatureDeclaration declaration);

        /// <summary>
        ///     Adds the feature's data-only contribution items. The platform realises them after
        ///     every feature this one requires has been realised.
        /// </summary>
        /// <param name="contribution">The contribution to add items to.</param>
        void Contribute(FeatureContribution contribution);
    }
}
