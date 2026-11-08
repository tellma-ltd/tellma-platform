// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Validation
{
    /// <summary>A validation path is an immutable segment list that compares by value and renders the bracket form.</summary>
    public class ValidationPathTests
    {
        [Fact]
        public void The_root_has_no_segments_and_renders_empty()
        {
            Assert.Empty(ValidationPath.Root.Segments);
            Assert.Equal(string.Empty, ValidationPath.Root.ToString());
        }

        [Fact]
        public void A_path_renders_the_bracket_form()
        {
            ValidationPath child = ValidationPath.Root.Index(3).Property("Lines").Index(1).Property("Quantity");

            Assert.Equal("[3].Lines[1].Quantity", child.ToString());
            Assert.Equal("Ids[2]", ValidationPath.Root.Property("Ids").Index(2).ToString());
            Assert.Equal("Arguments.CreditLimit", ValidationPath.Root.Property("Arguments").Property("CreditLimit").ToString());
        }

        [Fact]
        public void A_path_is_its_segments_in_order()
        {
            ValidationPath path = ValidationPath.Root.Index(0).Property("ParentId");

            Assert.Equal([new IndexSegment(0), new PropertySegment("ParentId")], path.Segments);
            Assert.True(path.Segments[0] is IndexSegment(0));
        }

        [Fact]
        public void Two_paths_built_apart_to_the_same_value_are_equal()
        {
            ValidationPath built = ValidationPath.Root.Index(2).Property("Name");
            ValidationPath constructed = new([new IndexSegment(2), new PropertySegment("Name")]);

            Assert.Equal(built, constructed);
            Assert.Equal(built.GetHashCode(), constructed.GetHashCode());
            Assert.True(built == constructed);

            Dictionary<ValidationPath, string> cells = new() { [built] = "B7" };
            Assert.Equal("B7", cells[constructed]);
        }

        [Fact]
        public void Paths_with_other_segments_differ()
        {
            ValidationPath path = ValidationPath.Root.Index(2).Property("Name");

            Assert.NotEqual(path, ValidationPath.Root.Index(3).Property("Name"));
            Assert.NotEqual(path, ValidationPath.Root.Index(2).Property("name"));
            Assert.NotEqual(path, ValidationPath.Root.Index(2));
            Assert.NotEqual(ValidationPath.Root.Property("Ids").Index(2), ValidationPath.Root.Index(2).Property("Ids"));
        }

        [Fact]
        public void A_path_cannot_be_changed_after_it_is_built()
        {
            PathSegment[] segments = [new IndexSegment(1)];
            ValidationPath path = new(segments);

            segments[0] = new IndexSegment(9);

            Assert.Equal(new IndexSegment(1), path.Segments[0]);
            Assert.IsNotType<PathSegment[]>(path.Segments);

            // Extending a path leaves the original as it was.
            _ = path.Property("Name");
            Assert.Single(path.Segments);
        }

        [Fact]
        public void An_index_is_zero_or_positive()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => ValidationPath.Root.Index(-1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new IndexSegment(-1));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void A_property_name_is_never_blank(string name)
        {
            _ = Assert.Throws<ArgumentException>(() => ValidationPath.Root.Property(name));
            _ = Assert.Throws<ArgumentNullException>(() => ValidationPath.Root.Property(null!));
        }

        [Fact]
        public void A_dotted_key_is_one_segment()
        {
            // A setting key is dotted by grammar, and an error on it points under the entries member.
            ValidationPath built = ValidationPath.Root.Property("Entries").Property("gl.posting.autoNumberOnPost");
            ValidationPath constructed = new([new PropertySegment("Entries"), new PropertySegment("gl.posting.autoNumberOnPost")]);

            Assert.Equal(2, built.Segments.Count);
            Assert.Equal("gl.posting.autoNumberOnPost", ((PropertySegment)built.Segments[1]).Name);
            Assert.Equal(constructed, built);
            Assert.NotEqual(ValidationPath.Root.Property("Entries").Property("gl").Property("posting").Property("autoNumberOnPost"), built);
            Assert.Equal("Entries.gl.posting.autoNumberOnPost", built.ToString());
        }

        [Fact]
        public void The_segment_list_is_required_and_holds_no_null()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new ValidationPath(null!));
            _ = Assert.Throws<ArgumentException>(() => new ValidationPath([null!]));
        }
    }
}
