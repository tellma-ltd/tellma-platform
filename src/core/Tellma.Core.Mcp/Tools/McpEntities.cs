// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Mcp.Tools
{
    /// <summary>An entity the tools reach: a stack, or a child query surface under its owner's exposure.</summary>
    /// <param name="Name">The entity's name as registered, such as <c>gl.Center</c>.</param>
    /// <param name="Stack">The stack; null for a child entity.</param>
    /// <param name="Child">The child query surface; null for a stack.</param>
    internal sealed record McpEntity(string Name, StackDescriptor? Stack, ChildQueryDescriptor? Child)
    {
        /// <summary>The entity's type.</summary>
        public Type EntityType => Stack?.EntityType ?? Child!.EntityType;

        /// <summary>Whether the entity is a stack's child collection, queried through its own surface.</summary>
        public bool IsChild => Child is not null;
    }

    /// <summary>
    ///     The entities the tools reach, matched by name ignoring case: every stack and every child
    ///     query surface whose exposure is not <see cref="McpExposure.Hidden" />. A hidden entity is
    ///     answered exactly as an unknown one. Singleton; built once from the registry.
    /// </summary>
    internal sealed class McpEntities
    {
        /// <summary>The visible entities by name, ignoring case.</summary>
        private readonly FrozenDictionary<string, McpEntity> _byName;

        /// <summary>Builds the catalogue from the stack registry.</summary>
        /// <param name="registry">The stack registry.</param>
        public McpEntities(IStackRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            // --- The stacks, then the child surfaces, in the registry's order ---
            Stacks = [.. registry.Stacks.Where(static stack => stack.Mcp != McpExposure.Hidden)];
            Children = [.. registry.ChildQueries.Where(static child => child.Mcp != McpExposure.Hidden)];
            List<McpEntity> all =
            [
                .. Stacks.Select(static stack => new McpEntity(stack.Resource, stack, null)),
                .. Children.Select(static child => new McpEntity(child.Resource, null, child)),
            ];

            // Entity names are unique ordinally; two that differ in case alone would make an agent's
            // spelling ambiguous, and the first registered keeps the name.
            Dictionary<string, McpEntity> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach (McpEntity entity in all)
            {
                _ = byName.TryAdd(entity.Name, entity);
            }

            _byName = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The visible stacks, in the registry's order.</summary>
        public IReadOnlyList<StackDescriptor> Stacks { get; }

        /// <summary>The visible child query surfaces, in the registry's order.</summary>
        public IReadOnlyList<ChildQueryDescriptor> Children { get; }

        /// <summary>Finds a visible entity by name, ignoring case.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The entity, or null when no visible entity has the name.</returns>
        public McpEntity? Find(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return _byName.GetValueOrDefault(name);
        }

        /// <summary>Finds a visible entity an argument names, or refuses the call.</summary>
        /// <param name="name">The name.</param>
        /// <param name="argument">The argument that named it.</param>
        /// <returns>The entity.</returns>
        /// <exception cref="BadRequestException">No visible entity has the name.</exception>
        public McpEntity Require(string name, string argument)
        {
            return Find(name) ?? throw ToolArguments.Invalid(
                argument,
                $"No entity is named '{name}'. Call {McpToolNames.Describe} without arguments for the entities this server serves.");
        }

        /// <summary>Finds a visible stack an argument names, or refuses the call.</summary>
        /// <param name="name">The name.</param>
        /// <param name="argument">The argument that named it.</param>
        /// <param name="operation">The operation the stack must serve.</param>
        /// <param name="tool">The tool, as the refusal names it.</param>
        /// <returns>The stack.</returns>
        /// <exception cref="BadRequestException">No visible entity has the name, it is a child entity, or its stack does not serve the operation.</exception>
        public StackDescriptor RequireStack(string name, string argument, StackOperations operation, string tool)
        {
            McpEntity entity = Require(name, argument);
            StackDescriptor stack = entity.Stack ?? throw ToolArguments.Invalid(
                argument,
                $"'{entity.Name}' is a child entity, which {tool} does not take; get its owner '{entity.Child!.OwnerResource}', or query '{entity.Name}' with {McpToolNames.Query}.");

            return stack.Operations.HasFlag(operation)
                ? stack
                : throw ToolArguments.Invalid(argument, $"The entity '{stack.Resource}' does not serve {tool}.");
        }
    }
}
