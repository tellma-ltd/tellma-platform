// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Abstractions.Tests.Caching
{
    /// <summary>
    ///     Version tags: equality only, the empty tag, fresh tags, the wire form a client keys its
    ///     stored shapes by, the tag names and snapshots.
    /// </summary>
    public class VersionTagTests
    {
        [Fact]
        public void The_wire_form_is_the_format_version_a_dot_and_32_lower_case_hex_digits()
        {
            VersionTag tag = new(Guid.Parse("3F2504E0-4F89-41D3-9A0C-0305E82C3301"));

            Assert.Equal("1.3f2504e04f8941d39a0c0305e82c3301", tag.ToWire(1));
            Assert.Equal("12.3f2504e04f8941d39a0c0305e82c3301", tag.ToWire(12));
            Assert.Equal("1.00000000000000000000000000000000", VersionTag.None.ToWire(1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void A_format_version_below_1_is_refused(int formatVersion)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => VersionTag.New().ToWire(formatVersion));
        }

        [Fact]
        public void Tags_compare_by_value_and_fresh_tags_are_distinct_from_none_and_each_other()
        {
            var value = Guid.NewGuid();

            Assert.Equal(new VersionTag(value), new VersionTag(value));
            Assert.Equal(Guid.Empty, VersionTag.None.Value);
            Assert.Same(VersionTag.None, VersionTag.None);
            Assert.NotEqual(VersionTag.None, VersionTag.New());
            Assert.NotEqual(VersionTag.New(), VersionTag.New());
        }

        [Fact]
        public void The_platform_names_and_the_entity_form_are_pinned()
        {
            Assert.Equal("settings", VersionTagNames.Settings);
            Assert.Equal("permissions", VersionTagNames.Permissions);
            Assert.Equal("entities", VersionTagNames.Entities);
            Assert.Equal("entity:", VersionTagNames.EntityPrefix);
            Assert.Equal(128, VersionTagNames.MaxNameLength);
            Assert.Equal("entity:gl.Center", VersionTagNames.ForEntity("gl.Center"));
        }

        [Fact]
        public void An_entity_name_that_is_blank_or_too_long_has_no_tag_name()
        {
            Assert.ThrowsAny<ArgumentException>(() => VersionTagNames.ForEntity(null!));
            Assert.ThrowsAny<ArgumentException>(() => VersionTagNames.ForEntity(" "));
            Assert.Equal(128, VersionTagNames.ForEntity(new string('a', 121)).Length);
            Assert.Throws<ArgumentException>(() => VersionTagNames.ForEntity(new string('a', 122)));
        }

        [Fact]
        public void A_snapshot_answers_none_for_a_name_it_lacks_and_compares_names_ordinally()
        {
            var settings = VersionTag.New();
            VersionTagSnapshot snapshot = new(7, new Dictionary<string, VersionTag> { ["settings"] = settings }, DateTimeOffset.UnixEpoch);

            Assert.Same(settings, snapshot["settings"]);
            Assert.True(snapshot.Has("settings"));
            Assert.Same(VersionTag.None, snapshot["Settings"]);
            Assert.False(snapshot.Has("Settings"));
            Assert.Same(VersionTag.None, snapshot["entities"]);
            Assert.False(snapshot.Has("entities"));
            Assert.Throws<ArgumentNullException>(() => snapshot[null!]);
            Assert.Throws<ArgumentNullException>(() => snapshot.Has(null!));
        }

        [Fact]
        public void A_snapshot_copies_its_tags()
        {
            Dictionary<string, VersionTag> tags = new() { ["settings"] = VersionTag.New() };
            VersionTagSnapshot snapshot = new(7, tags, DateTimeOffset.UnixEpoch);

            tags["entities"] = VersionTag.New();
            tags.Remove("settings");

            Assert.True(snapshot.Has("settings"));
            Assert.False(snapshot.Has("entities"));
            Assert.Throws<ArgumentNullException>(() => new VersionTagSnapshot(7, null!, DateTimeOffset.UnixEpoch));
        }

        [Fact]
        public void A_users_tags_are_the_two_stamp_columns()
        {
            Assert.Equal(["Permissions", "Preferences"], Enum.GetNames<UserVersionTagNames>());
            Assert.Equal(["Rerun", "Refresh"], Enum.GetNames<VersionTagMismatchPolicy>());
        }
    }
}
