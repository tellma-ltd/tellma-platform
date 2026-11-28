// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Collects what the securable contributors register: securables, aliases of renamed resources,
    ///     and changes to the set of sensitive securables. The platform composes the registry from it
    ///     and freezes it; every call after that throws.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The builder records every call as made. The registry, and the startup gate over it,
    ///         judge what was recorded: the grammar of the names, the same pair registered with
    ///         different filter roots or owners, a pair marked both sensitive and not sensitive, and
    ///         an alias or a mark naming nothing registered are startup problems, not exceptions.
    ///     </para>
    ///     <para>
    ///         A resource is two or more segments of an ASCII letter followed by ASCII letters or
    ///         digits, joined by dots, at most 128 characters (<c>gl.Center</c>,
    ///         <c>core.Settings.General</c>); an action is a PascalCase identifier of at most 32
    ///         characters (<c>Save</c>). Both compare ignoring case and keep the spelling registered.
    ///         <c>*</c> is the wildcard of stored permissions and never a registered name.
    ///     </para>
    /// </remarks>
    public sealed class SecurableRegistryBuilder
    {
        private readonly List<SecurableDeclaration> _securables = [];
        private readonly List<SecurableAliasDeclaration> _aliases = [];
        private readonly List<SecurableMarkDeclaration> _marks = [];
        private bool _frozen;

        /// <summary>Creates an open builder; the platform creates the one every contributor writes to.</summary>
        internal SecurableRegistryBuilder()
        {
        }

        /// <summary>The name of the feature whose contributor is running, which owns what it records.</summary>
        internal string Contributor { get; set; } = "core";

        /// <summary>The securables recorded, in call order.</summary>
        internal IReadOnlyList<SecurableDeclaration> Securables => _securables;

        /// <summary>The aliases recorded, in call order.</summary>
        internal IReadOnlyList<SecurableAliasDeclaration> Aliases => _aliases;

        /// <summary>The sensitivity marks recorded, in call order.</summary>
        internal IReadOnlyList<SecurableMarkDeclaration> Marks => _marks;

        /// <summary>
        ///     Registers a securable. The same pair with the same filter root and owner is registered
        ///     once, whoever registers it again; with another filter root or owner it is a startup
        ///     problem.
        /// </summary>
        /// <param name="resource">The resource, such as <c>gl.Center</c>; an entity's resource is its entity name.</param>
        /// <param name="action">The action, such as <c>Save</c>.</param>
        /// <param name="filterRoot">The query entity a grant's filter binds against, such as <c>gl.Center</c>; null when grants take no filter.</param>
        /// <param name="isSensitive">Whether exercising it asks for a recent sign-in; any registration that says so makes it sensitive.</param>
        /// <param name="owner">The owner of a child securable, through which grants on the owner reach it; null for any other.</param>
        /// <param name="feature">The name of the feature it belongs to; null for the feature whose contributor registers it.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="resource" /> or <paramref name="action" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="owner" /> has a null resource or navigation.</exception>
        /// <exception cref="InvalidOperationException">The registry is already composed.</exception>
        public SecurableRegistryBuilder Add(
            string resource,
            string action,
            string? filterRoot,
            bool isSensitive = false,
            SecurableOwner? owner = null,
            string? feature = null)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);
            if (owner is not null && (owner.Resource is null || owner.Navigation is null))
            {
                throw new ArgumentException("An owner names both its resource and its navigation.", nameof(owner));
            }

            ThrowIfFrozen();
            _securables.Add(new SecurableDeclaration(resource, action, filterRoot, isSensitive, owner, feature, Contributor));
            return this;
        }

        /// <summary>
        ///     Declares that a resource was renamed: a stored permission naming
        ///     <paramref name="oldResource" /> resolves to <paramref name="newResource" /> while the
        ///     migrator rewrites the stored permissions. The old name is no registered resource; the new
        ///     one is.
        /// </summary>
        /// <param name="oldResource">The resource's former name.</param>
        /// <param name="newResource">The registered resource it now is.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">A name is null.</exception>
        /// <exception cref="InvalidOperationException">The registry is already composed.</exception>
        public SecurableRegistryBuilder Alias(string oldResource, string newResource)
        {
            ArgumentNullException.ThrowIfNull(oldResource);
            ArgumentNullException.ThrowIfNull(newResource);
            ThrowIfFrozen();
            _aliases.Add(new SecurableAliasDeclaration(oldResource, newResource, Contributor));
            return this;
        }

        /// <summary>Adds a registered securable to the sensitive set, which asks for a recent sign-in.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">A name is null.</exception>
        /// <exception cref="InvalidOperationException">The registry is already composed.</exception>
        public SecurableRegistryBuilder MarkSensitive(string resource, string action)
        {
            return Mark(resource, action, sensitive: true);
        }

        /// <summary>
        ///     Removes a registered securable from the sensitive set, the platform's defaults included.
        ///     Marking one pair both sensitive and not sensitive is a startup problem.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">A name is null.</exception>
        /// <exception cref="InvalidOperationException">The registry is already composed.</exception>
        public SecurableRegistryBuilder MarkNotSensitive(string resource, string action)
        {
            return Mark(resource, action, sensitive: false);
        }

        /// <summary>Closes the builder: every later call throws.</summary>
        internal void Freeze()
        {
            _frozen = true;
        }

        /// <summary>Records a sensitivity mark.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="sensitive">Whether the mark adds the pair to the sensitive set or removes it.</param>
        /// <returns>This builder.</returns>
        private SecurableRegistryBuilder Mark(string resource, string action, bool sensitive)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);
            ThrowIfFrozen();
            _marks.Add(new SecurableMarkDeclaration(resource, action, sensitive, Contributor));
            return this;
        }

        /// <summary>Throws once the registry is composed.</summary>
        /// <exception cref="InvalidOperationException">The builder is frozen.</exception>
        private void ThrowIfFrozen()
        {
            if (_frozen)
            {
                throw new InvalidOperationException(
                    "The securable registry is already composed and immutable. Register securables from an ISecurableContributor or contribution.Securables(...), which run while it is composed.");
            }
        }
    }
}
