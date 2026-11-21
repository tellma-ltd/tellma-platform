// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>Request ids narrow to a stack's key type, and an id the key cannot hold is a bad request naming its path.</summary>
    public class WireIdsTests
    {
        [Fact]
        public void Ids_narrow_to_an_int_key_in_order()
        {
            IReadOnlyList<int> keys = WireIds.ToKeys<int>([5, int.MaxValue, int.MinValue], "ids");

            Assert.Equal([5, int.MaxValue, int.MinValue], keys);
        }

        [Fact]
        public void Ids_for_a_long_key_are_the_request_ids()
        {
            IReadOnlyList<long> ids = [5, 9_007_199_254_740_993];

            Assert.Same(ids, WireIds.ToKeys<long>(ids, "ids"));
            Assert.Equal(9_007_199_254_740_993L, WireIds.ToKey<long>(9_007_199_254_740_993, "id"));
        }

        [Fact]
        public void An_id_outside_an_int_key_is_a_bad_request_naming_its_index()
        {
            BadRequestException refusal = Assert.Throws<BadRequestException>(() =>
                WireIds.ToKeys<int>([1, 2, 3_000_000_000], "ids"));

            Assert.Equal("bad-request", refusal.Code);
            Assert.Equal("ids[2]", refusal.Arguments["path"]);
            Assert.Contains("ids[2]", refusal.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public void A_single_id_outside_an_int_key_names_its_path()
        {
            BadRequestException refusal = Assert.Throws<BadRequestException>(() => WireIds.ToKey<int>(-3_000_000_000, "id"));

            Assert.Equal("id", refusal.Arguments["path"]);
            Assert.Equal(7, WireIds.ToKey<int>(7, "id"));
        }

        [Fact]
        public void A_key_type_no_stack_has_is_refused()
        {
            _ = Assert.Throws<NotSupportedException>(() => WireIds.ToKeys<Guid>([1], "ids"));
            _ = Assert.Throws<NotSupportedException>(() => WireIds.ToKey<short>(1, "id"));
        }

        [Fact]
        public void The_ids_and_the_path_are_required()
        {
            _ = Assert.Throws<ArgumentNullException>(() => WireIds.ToKeys<int>(null!, "ids"));
            _ = Assert.Throws<ArgumentException>(() => WireIds.ToKeys<int>([1], " "));
            _ = Assert.Throws<ArgumentException>(() => WireIds.ToKey<int>(1, ""));
        }
    }
}
