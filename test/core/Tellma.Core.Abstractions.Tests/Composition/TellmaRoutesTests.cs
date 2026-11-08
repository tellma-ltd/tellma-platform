// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tests.Composition
{
    /// <summary>The reserved route words: their exact lists, and that no caller can change them.</summary>
    public class TellmaRoutesTests
    {
        [Fact]
        public void The_reserved_words_are_exactly_the_platform_surfaces_and_core_pages()
        {
            string[] serverFirst = ["api", "bff", "health", "id", "mcp", "openapi", "signin-oidc", "signout-callback-oidc", ".well-known"];
            string[] serverTenant = ["api", "hub", "blobs", "mcp"];
            string[] spaFirst = ["tenants", "signed-out"];
            string[] spaTenant = ["settings", "me", "list"];

            Assert.Equal(serverFirst, TellmaRoutes.ServerFirstSegments);
            Assert.Equal(serverTenant, TellmaRoutes.ServerTenantSegments);
            Assert.Equal(spaFirst, TellmaRoutes.SpaFirstSegments);
            Assert.Equal(spaTenant, TellmaRoutes.SpaTenantSegments);
            Assert.Equal("new", TellmaRoutes.NewSegment);
        }

        [Fact]
        public void The_server_and_client_never_reserve_the_same_word_at_the_same_position()
        {
            Assert.Empty(TellmaRoutes.ServerFirstSegments.Intersect(TellmaRoutes.SpaFirstSegments));
            Assert.Empty(TellmaRoutes.ServerTenantSegments.Intersect(TellmaRoutes.SpaTenantSegments));
        }

        [Fact]
        public void Every_word_is_a_lowercase_route_segment_that_no_tenant_id_can_equal()
        {
            IEnumerable<string> words = TellmaRoutes.ServerFirstSegments
                .Concat(TellmaRoutes.ServerTenantSegments)
                .Concat(TellmaRoutes.SpaFirstSegments)
                .Concat(TellmaRoutes.SpaTenantSegments)
                .Append(TellmaRoutes.NewSegment);

            Assert.All(words, static word =>
            {
                Assert.Equal(word.ToLowerInvariant(), word);
                Assert.False(int.TryParse(word, out _));
                Assert.DoesNotContain('/', word);
            });
        }

        [Fact]
        public void The_lists_cannot_be_changed_through_a_cast()
        {
            IList<string> list = Assert.IsAssignableFrom<IList<string>>(TellmaRoutes.ServerFirstSegments);

            Assert.True(list.IsReadOnly);
            _ = Assert.Throws<NotSupportedException>(() => list[0] = "anything");
        }
    }
}
