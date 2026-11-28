// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Composition;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud
{
    /// <summary>
    ///     Renders the stack registry as text, one block per stack, child query surface and routed
    ///     service, so a golden pins every member the registry derives.
    /// </summary>
    internal static class StackDescriptorText
    {
        /// <summary>Renders the registry.</summary>
        /// <param name="registry">The registry.</param>
        /// <returns>The text, with LF line endings.</returns>
        public static string Render(IStackRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            StringBuilder text = new();
            foreach (StackDescriptor stack in registry.Stacks)
            {
                Stack(text, stack);
            }

            foreach (ChildQueryDescriptor child in registry.ChildQueries)
            {
                Child(text, child);
            }

            foreach (ApiServiceDescriptor service in registry.Services)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"service {service.Route}\n");
                Line(text, "class", TypeDisplay.Qualified(service.ServiceType));
                Line(text, "feature", service.Feature);
                Actions(text, service.Actions);
            }

            foreach (EnlistmentDescriptor enlistment in registry.Enlistments)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"enlistment {enlistment.WriterName} -> {enlistment.TargetResource}\n");
            }

            return text.ToString();
        }

        /// <summary>Renders a stack.</summary>
        /// <param name="text">Receives the text.</param>
        /// <param name="stack">The stack.</param>
        private static void Stack(StringBuilder text, StackDescriptor stack)
        {
            _ = text.Append(CultureInfo.InvariantCulture, $"stack {stack.Resource}\n");
            Line(text, "segment", stack.ResourceSegment);
            Line(text, "entity", TypeDisplay.Qualified(stack.EntityType));
            Line(text, "service", TypeDisplay.Qualified(stack.ServiceType));
            Line(text, "key", TypeDisplay.Short(stack.KeyType));
            Line(text, "description", stack.Description ?? "-");
            Line(text, "operations", stack.Operations.ToString());
            Line(text, "capabilities", List(stack.Capabilities));
            Line(text, "properties", List(stack.Properties.Select(static property => $"{property.Name}:{property.Ownership}")));
            Line(text, "children", List(stack.Children.Select(static child => $"{child.Property}({TypeDisplay.Short(child.ChildType)} by {child.ParentKeyProperty}, max {child.MaxCount})")));
            Line(text, "searchable", List(stack.SearchableProperties.Select(static searchable => $"{searchable.Property}:{searchable.Kind}")));
            Line(text, "natural-keys", List(stack.NaturalKeys));
            Line(text, "default-select", stack.DefaultSelect);
            Line(text, "related-select", List(stack.RelatedSelect));
            Line(text, "details-expand", List(stack.DetailsExpand));
            Line(text, "default-order-by", stack.DefaultOrderBy ?? "-");
            Line(text, "display-name", $"{stack.DisplayName.Source.Name} group {stack.DisplayName.Group?.Name ?? "-"} prefix {stack.DisplayName.Prefix?.Name ?? "-"}");
            Line(text, "extras", List(stack.Extras));
            Line(text, "named-filters", List(stack.NamedFilters.Select(static filter => $"{filter.Key}={Tree(filter.Value)}")));
            Line(text, "limits", Limits(stack.Limits));
            Line(text, "mcp", $"{stack.Mcp}, revision {stack.ContractRevision}");
            Line(text, "feature", stack.Feature);
            Actions(text, stack.Actions);
        }

        /// <summary>Renders a child query surface.</summary>
        /// <param name="text">Receives the text.</param>
        /// <param name="child">The surface.</param>
        private static void Child(StringBuilder text, ChildQueryDescriptor child)
        {
            _ = text.Append(CultureInfo.InvariantCulture, $"child {child.Resource}\n");
            Line(text, "segment", child.ResourceSegment);
            Line(text, "entity", TypeDisplay.Qualified(child.EntityType));
            Line(text, "owner", $"{child.OwnerResource} through {child.OwnerNavigation}, collection {child.Collection}");
            Line(text, "searchable", List(child.SearchableProperties.Select(static searchable => $"{searchable.Property}:{searchable.Kind}")));
            Line(text, "default-select", child.DefaultSelect);
            Line(text, "related-select", List(child.RelatedSelect));
            Line(text, "default-order-by", child.DefaultOrderBy);
            Line(text, "exportable", child.Exportable ? "yes" : "no");
            Line(text, "limits", Limits(child.Limits));
            Line(text, "mcp", child.Mcp.ToString());
            Actions(text, child.Actions);
        }

        /// <summary>Renders actions, one line each, every flag named.</summary>
        /// <param name="text">Receives the text.</param>
        /// <param name="actions">The actions.</param>
        private static void Actions(StringBuilder text, IEnumerable<ActionDescriptor> actions)
        {
            foreach (ActionDescriptor action in actions)
            {
                List<string> flags = [];
                AddIf(flags, action.Mutation, "mutation");
                AddIf(flags, action.AllowWhenReadOnly, "read-only-ok");
                AddIf(flags, action.Idempotent, "idempotent");
                AddIf(flags, action.Long, "long");
                AddIf(flags, action.Destructive, "destructive");
                string kind = action switch
                {
                    EntityActionDescriptor entity => $"entity {entity.Action}{(entity.IsBuiltIn ? " built-in" : string.Empty)}"
                        + $"{(entity.SupportsFilter ? " filtered" : string.Empty)}{(entity.LoadChildren ? " children" : string.Empty)}"
                        + $"{(entity.PostCheck ? " post-check" : string.Empty)}{(entity.SingleTarget ? " single" : string.Empty)}"
                        + $" args {(entity.ArgumentsType is Type arguments ? TypeDisplay.Short(arguments) : "-")}",
                    ApiActionDescriptor api => $"api {(api.Securable is SecurableRef securable ? $"{securable.Resource}:{securable.Action}" : "member-only")}"
                        + $" body {(api.RequestType is Type request ? TypeDisplay.Short(request) : "-")} handler {TypeDisplay.Short(api.HandlerType)}",
                    _ => throw new InvalidOperationException("Unknown action kind."),
                };
                _ = text.Append(CultureInfo.InvariantCulture, $"  action {action.Name}: {kind}; {List(flags)}; result {TypeDisplay.Short(action.ResultType)}; mcp {action.Mcp}, revision {action.ContractRevision}; {action.Description ?? "-"}\n");
            }
        }

        /// <summary>Renders a filter tree.</summary>
        /// <param name="tree">The tree.</param>
        /// <returns>The text.</returns>
        private static string Tree(FilterTree tree)
        {
            return tree switch
            {
                FilterTree.LeafNode leaf => $"({leaf.Text})",
                FilterTree.AndNode and => $"and[{string.Join(", ", and.Children.Select(Tree))}]",
                FilterTree.OrNode or => $"or[{string.Join(", ", or.Children.Select(Tree))}]",
                FilterTree.NotNode not => $"not{Tree(not.Operand)}",
                FilterTree.ViaNode via => $"via {via.Navigation} {Tree(via.Inner)}",
                FilterTree.ExistsNode exists => $"exists {exists.Entity}.{exists.Navigation} {Tree(exists.Inner)}",
                _ => throw new InvalidOperationException("Unknown filter node."),
            };
        }

        /// <summary>Renders ceilings: the defaults by name, others in full.</summary>
        /// <param name="limits">The ceilings.</param>
        /// <returns>The text.</returns>
        private static string Limits(StackLimits limits)
        {
            return limits == StackLimits.Default ? "default" : limits.ToString();
        }

        /// <summary>Renders a list, or a dash when it is empty.</summary>
        /// <param name="items">The items.</param>
        /// <returns>The text.</returns>
        private static string List(IEnumerable<string> items)
        {
            string joined = string.Join(", ", items);
            return joined.Length == 0 ? "-" : joined;
        }

        /// <summary>Appends one indented member line.</summary>
        /// <param name="text">Receives it.</param>
        /// <param name="name">The member.</param>
        /// <param name="value">Its value.</param>
        private static void Line(StringBuilder text, string name, string value)
        {
            _ = text.Append(CultureInfo.InvariantCulture, $"  {name}: {value}\n");
        }

        /// <summary>Adds a flag when it is set.</summary>
        /// <param name="flags">The flags.</param>
        /// <param name="set">Whether it is set.</param>
        /// <param name="flag">Its name.</param>
        private static void AddIf(List<string> flags, bool set, string flag)
        {
            if (set)
            {
                flags.Add(flag);
            }
        }
    }
}
