// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Validation;
using Tellma.Core.AspNetCore.Problems;

namespace Tellma.Core.AspNetCore.Tests.Problems
{
    /// <summary>
    ///     The wire grammar of a validation path: camelCase properties, bracketed indexes, the
    ///     payload's member at the head, and the type that declares the property a message labels.
    /// </summary>
    public sealed class ValidationPathsTests
    {
        [Fact]
        public void A_save_rows_path_starts_at_entities_and_camel_cases_each_property()
        {
            ValidationPath path = ValidationPath.Root.Index(3).Property("RoleMemberships").Index(1).Property("RoleId");

            Assert.Equal("entities[3].roleMemberships[1].roleId", ValidationPaths.Render(path, ValidationRoot.Entities));
        }

        [Fact]
        public void An_action_target_path_starts_at_ids()
        {
            Assert.Equal("ids[2]", ValidationPaths.Render(ValidationPath.Root.Index(2), ValidationRoot.Ids));
            Assert.Equal("ids[0].name", ValidationPaths.Render(ValidationPath.Root.Index(0).Property("Name"), ValidationRoot.Ids));
        }

        [Fact]
        public void A_single_target_action_names_its_one_target_id()
        {
            // A leading index of 0 and the request's own Ids[0] both name the one target.
            Assert.Equal("id", ValidationPaths.Render(ValidationPath.Root.Index(0), ValidationRoot.SingleId));
            Assert.Equal("id", ValidationPaths.Render(ValidationPath.Root.Property("Ids").Index(0), ValidationRoot.SingleId));
            Assert.Equal("id.name", ValidationPaths.Render(ValidationPath.Root.Index(0).Property("Name"), ValidationRoot.SingleId));
        }

        [Fact]
        public void An_argument_path_is_camel_cased_under_arguments()
        {
            ValidationPath path = ValidationPath.Root.Property("Arguments").Property("PostingDate");

            Assert.Equal("arguments.postingDate", ValidationPaths.Render(path, ValidationRoot.Ids));
            Assert.Equal("arguments.postingDate", ValidationPaths.Render(path, ValidationRoot.Body));
        }

        [Theory]
        [InlineData(nameof(ValidationRoot.Body))]
        [InlineData(nameof(ValidationRoot.Entities))]
        [InlineData(nameof(ValidationRoot.Ids))]
        [InlineData(nameof(ValidationRoot.SingleId))]
        public void The_empty_path_is_the_whole_body(string root)
        {
            Assert.Equal(ValidationPaths.BodyPath, ValidationPaths.Render(ValidationPath.Root, Enum.Parse<ValidationRoot>(root)));
        }

        [Fact]
        public void A_body_without_rows_keeps_a_leading_index_under_body()
        {
            Assert.Equal("body[1].code", ValidationPaths.Render(ValidationPath.Root.Index(1).Property("Code"), ValidationRoot.Body));
        }

        [Fact]
        public void The_owner_of_a_nested_property_is_its_list_element_type()
        {
            ValidationPath child = ValidationPath.Root.Index(0).Property(nameof(Parent.Children)).Index(2).Property(nameof(Child.Label));
            ValidationPath own = ValidationPath.Root.Index(0).Property(nameof(Parent.Name));

            Assert.Equal(typeof(Child), ValidationPaths.OwnerOfLastProperty(child, ValidationRoot.Entities, typeof(Parent)));
            Assert.Equal(typeof(Parent), ValidationPaths.OwnerOfLastProperty(own, ValidationRoot.Entities, typeof(Parent)));
        }

        [Fact]
        public void A_path_outside_the_row_type_has_no_owner()
        {
            Assert.Null(ValidationPaths.OwnerOfLastProperty(ValidationPath.Root.Index(0).Property("Missing"), ValidationRoot.Entities, typeof(Parent)));
            Assert.Null(ValidationPaths.OwnerOfLastProperty(ValidationPath.Root.Index(0), ValidationRoot.Entities, typeof(Parent)));
            Assert.Null(ValidationPaths.OwnerOfLastProperty(ValidationPath.Root.Index(0).Property(nameof(Parent.Name)), ValidationRoot.Body, typeof(Parent)));
            Assert.Null(ValidationPaths.OwnerOfLastProperty(ValidationPath.Root.Index(0).Property(nameof(Parent.Name)), ValidationRoot.Entities, rowType: null));
        }

        /// <summary>A row with a child list.</summary>
        /// <param name="Name">A scalar.</param>
        /// <param name="Children">The child rows.</param>
        private sealed record Parent(string Name, IReadOnlyList<Child> Children);

        /// <summary>A child row.</summary>
        /// <param name="Label">A scalar.</param>
        private sealed record Child(string Label);
    }
}
