// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.stack-projections</c>: every projection text of every stack and child query
    ///     surface validates against the tenant-independent query schema: the default and related
    ///     selects as value lists, the default ordering as an ordering, the details expansion as
    ///     navigation paths through the aggregate, and every named filter's tree as a filter over the
    ///     entity each node applies to.
    /// </summary>
    /// <remarks>
    ///     A model that does not build is the entity-metadata check's to report; the texts are then
    ///     not judged.
    /// </remarks>
    internal sealed class StackProjectionsCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.StackProjections;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            if (StackCheckSupport.Registry(services, Name, problems) is not StackRegistry { Failed: false } registry
                || services.GetService<EntityMetadataSource>()?.Build.Provider is not IEntityMetadataProvider metadata
                || services.GetService<IQueryexSchemaProvider>() is not IQueryexSchemaProvider schemas)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            Validator validator = new(services.GetService<QueryexEngine>() ?? new QueryexEngine(), schemas.GetSchema(MultilingualShape.All), Name, problems);
            foreach (StackDescriptor stack in registry.Stacks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (validator.Root(stack.Resource, $"The stack of {stack.Resource}") is not EntityDescriptor root)
                {
                    continue;
                }

                string named = $"of the stack of {stack.Resource}";
                validator.Value(stack.DefaultSelect, root, $"The default select {named}", directions: false);
                validator.Value(string.Join(", ", stack.RelatedSelect), root, $"The related select {named}", directions: false);
                if (stack.DefaultOrderBy is string orderBy)
                {
                    validator.Value(orderBy, root, $"The default ordering {named}", directions: true);
                }

                CheckExpand(stack, metadata.Get(stack.EntityType), problems);
                foreach ((string name, FilterTree tree) in stack.NamedFilters)
                {
                    validator.Filter(tree, root, $"The named filter '{name}' {named}");
                }
            }

            foreach (ChildQueryDescriptor child in registry.ChildQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (validator.Root(child.Resource, $"The child query surface of {child.Resource}") is not EntityDescriptor root)
                {
                    continue;
                }

                string named = $"of the child query surface of {child.Resource}";
                validator.Value(child.DefaultSelect, root, $"The default select {named}", directions: false);
                validator.Value(string.Join(", ", child.RelatedSelect), root, $"The related select {named}", directions: false);
                validator.Value(child.DefaultOrderBy, root, $"The default ordering {named}", directions: true);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>
        ///     Reports a details expansion path that does not walk the aggregate: each segment a
        ///     foreign-key navigation or a child collection of the entity the path has reached.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="metadata">The leaf's entity metadata.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckExpand(StackDescriptor stack, EntityMetadata metadata, List<CompositionProblem> problems)
        {
            foreach (string path in stack.DetailsExpand)
            {
                EntityMetadata? at = metadata;
                string[] segments = path.Split('.');
                for (int i = 0; i < segments.Length && at is not null; i++)
                {
                    string segment = segments[i].Trim();
                    EntityMetadata? next = at.References.FirstOrDefault(reference => string.Equals(reference.Navigation, segment, StringComparison.Ordinal))?.Target
                        ?? at.Children.FirstOrDefault(child => string.Equals(child.Property, segment, StringComparison.Ordinal))?.Child;
                    if (next is null)
                    {
                        problems.Add(new CompositionProblem(
                            Name,
                            $"The details expansion '{path}' of the stack of {stack.Resource} names '{segment}', which is neither a navigation nor a child collection of {at.Name}.",
                            "Name each segment after a navigation or a child collection of the entity the path has reached, such as 'Lines.Account'."));
                    }

                    at = next;
                }
            }
        }

        /// <summary>Validates projection texts and filter trees against one schema.</summary>
        /// <param name="engine">The Queryex engine.</param>
        /// <param name="schema">The tenant-independent schema.</param>
        /// <param name="check">The check's name, every problem's source.</param>
        /// <param name="problems">Receives the problems.</param>
        private sealed class Validator(QueryexEngine engine, QueryexSchema schema, string check, List<CompositionProblem> problems)
        {
            /// <summary>The query entity of a resource, reporting one the schema does not hold.</summary>
            /// <param name="resource">The resource.</param>
            /// <param name="subject">What is validated, for the problem.</param>
            /// <returns>The entity, or null.</returns>
            public EntityDescriptor? Root(string resource, string subject)
            {
                if (schema.FindEntity(resource) is EntityDescriptor root)
                {
                    return root;
                }

                problems.Add(new CompositionProblem(check, $"{subject} is over '{resource}', which is not an entity of the query schema.", "Map the entity in the tenant model, so queries can reach it."));
                return null;
            }

            /// <summary>Validates a value list, or an ordering when directions are accepted.</summary>
            /// <param name="text">The text.</param>
            /// <param name="root">The entity its paths resolve from.</param>
            /// <param name="subject">What is validated, for the problem.</param>
            /// <param name="directions">Whether it is an ordering.</param>
            public void Value(string text, EntityDescriptor root, string subject, bool directions)
            {
                Validate(text, root, QueryexMode.Value, QueryexLanguage.Version, directions, subject);
            }

            /// <summary>
            ///     Validates a filter tree: each leaf as a filter over the entity its node applies to,
            ///     each via node's path as to-one navigations, each exists node's entity and the
            ///     navigation it reaches the current entity by.
            /// </summary>
            /// <param name="tree">The tree.</param>
            /// <param name="root">The entity the tree filters.</param>
            /// <param name="subject">What is validated, for the problem.</param>
            public void Filter(FilterTree tree, EntityDescriptor root, string subject)
            {
                switch (tree)
                {
                    case FilterTree.LeafNode leaf:
                        Validate(leaf.Text, root, QueryexMode.Filter, leaf.QueryexVersion ?? QueryexLanguage.Version, directions: false, subject);
                        break;
                    case FilterTree.AndNode and:
                        foreach (FilterTree child in and.Children)
                        {
                            Filter(child, root, subject);
                        }

                        break;
                    case FilterTree.OrNode or:
                        foreach (FilterTree child in or.Children)
                        {
                            Filter(child, root, subject);
                        }

                        break;
                    case FilterTree.NotNode not:
                        Filter(not.Operand, root, subject);
                        break;
                    case FilterTree.ViaNode via:
                        EntityDescriptor? at = root;
                        foreach (string segment in via.Navigation.Split('.'))
                        {
                            at = at.Navigations.FirstOrDefault(navigation => string.Equals(navigation.Name, segment, StringComparison.OrdinalIgnoreCase))?.Target;
                            if (at is null)
                            {
                                problems.Add(new CompositionProblem(check, $"{subject} goes via '{via.Navigation}', whose segment '{segment}' is not a navigation of the entity it has reached.", "Name a dotted path of to-one navigations from the entity the node applies to."));
                                return;
                            }
                        }

                        Filter(via.Inner, at, subject);
                        break;
                    case FilterTree.ExistsNode exists:
                        if (schema.FindEntity(exists.Entity) is not EntityDescriptor related)
                        {
                            problems.Add(new CompositionProblem(check, $"{subject} tests the existence of '{exists.Entity}', which is not an entity of the query schema.", "Name an entity the tenant model maps."));
                            return;
                        }

                        if (related.Navigations.FirstOrDefault(navigation => string.Equals(navigation.Name, exists.Navigation, StringComparison.OrdinalIgnoreCase)) is not NavigationDescriptor back
                            || !ReferenceEquals(back.Target, root))
                        {
                            problems.Add(new CompositionProblem(check, $"{subject} tests the existence of '{exists.Entity}' through '{exists.Navigation}', which is not a navigation of {related.Name} to {root.Name}.", $"Name the navigation of {related.Name} whose target is {root.Name}."));
                            return;
                        }

                        Filter(exists.Inner, related, subject);
                        break;

                    // The tree's kinds are closed; FilterTree's constructor is not reachable outside its assembly.
                    default:
                        throw new InvalidOperationException($"The filter tree node {tree.GetType().Name} is not one the check knows.");
                }
            }

            /// <summary>Validates one text, reporting its diagnostics.</summary>
            /// <param name="text">The text.</param>
            /// <param name="root">The entity its paths resolve from.</param>
            /// <param name="mode">The position it is validated for.</param>
            /// <param name="version">The Queryex version it is written in.</param>
            /// <param name="directions">Whether direction suffixes are accepted.</param>
            /// <param name="subject">What is validated, for the problem.</param>
            private void Validate(string text, EntityDescriptor root, QueryexMode mode, int version, bool directions, string subject)
            {
                QueryexResult<ValidatedExpression> result = engine.Validate(text, new ValidationOptions
                {
                    Schema = schema,
                    Root = root,
                    Mode = mode,
                    QueryexVersion = version,
                    Directions = directions,
                });
                if (result.Succeeded)
                {
                    return;
                }

                string described = string.Join("; ", result.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code} at {diagnostic.Location ?? "the text"} [{diagnostic.Span.Start}..{diagnostic.Span.End})"
                    + (diagnostic.Arguments.Count == 0 ? string.Empty : $" ({string.Join(", ", diagnostic.Arguments.Select(static argument => $"{argument.Key}={argument.Value}"))})")));
                problems.Add(new CompositionProblem(check, $"{subject}, '{text}', does not validate against the query schema: {described}.", "Correct the text so every path names a property or navigation of the entity, as the query schema maps it."));
            }
        }
    }
}
