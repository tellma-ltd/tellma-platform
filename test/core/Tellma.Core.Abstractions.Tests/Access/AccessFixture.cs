// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Tests.Access
{
    /// <summary>
    ///     Hand-built securables, grants and sets for the composition tests, and a small query schema
    ///     their filters compile against: widgets with a kind, and lines under a header under a book.
    /// </summary>
    internal static class AccessFixture
    {
        /// <summary>The widgets' resource, which is its own filter root.</summary>
        public const string Widget = "fx.Widget";

        /// <summary>The kinds' resource.</summary>
        public const string Kind = "fx.Kind";

        /// <summary>The books' resource: the top of an owner chain.</summary>
        public const string Book = "fx.Book";

        /// <summary>The headers' resource, owned by a book.</summary>
        public const string Header = "fx.Header";

        /// <summary>The lines' resource, owned by a header, itself owned.</summary>
        public const string Line = "fx.Line";

        /// <summary>The instant most decisions are taken at.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

        /// <summary>The query schema of the fixture's entities.</summary>
        public static QueryexSchema Schema { get; } = BuildSchema();

        /// <summary>A securable of the fixture's feature.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="filterRoot">The filter root; null for a securable that takes no filter.</param>
        /// <param name="owner">The owner, for a child.</param>
        /// <returns>The securable.</returns>
        public static SecurableDescriptor Securable(string resource, string action, string? filterRoot = null, SecurableOwner? owner = null)
        {
            return new SecurableDescriptor(resource, action, filterRoot, false, "fixture", owner);
        }

        /// <summary>A filterable securable of the widgets.</summary>
        /// <param name="action">The action.</param>
        /// <returns>The securable, filtered on the widgets.</returns>
        public static SecurableDescriptor WidgetSecurable(string action)
        {
            return Securable(Widget, action, Widget);
        }

        /// <summary>A grant of a role, stamped with the current Queryex version when filtered.</summary>
        /// <param name="resource">The resource, or the wildcard.</param>
        /// <param name="action">The action, or the wildcard.</param>
        /// <param name="filter">The filter; null for an unrestricted grant.</param>
        /// <param name="permissionId">The permission.</param>
        /// <param name="roleId">The role.</param>
        /// <param name="isPublic">Whether the role is public.</param>
        /// <param name="startsAt">The membership's start.</param>
        /// <param name="endsAt">The membership's end.</param>
        /// <returns>The grant.</returns>
        public static RoleAccessGrant Grant(
            string resource,
            string action,
            string? filter = null,
            int permissionId = 1,
            int roleId = 10,
            bool isPublic = false,
            DateTimeOffset? startsAt = null,
            DateTimeOffset? endsAt = null)
        {
            return new RoleAccessGrant(
                resource,
                action,
                permissionId,
                roleId,
                $"Role {roleId}",
                isPublic,
                filter,
                filter is null ? null : QueryexLanguage.Version,
                startsAt,
                endsAt);
        }

        /// <summary>A caller's set of the given grants and problems.</summary>
        /// <param name="grants">The grants.</param>
        /// <returns>The set.</returns>
        public static UserAccess Set(params RoleAccessGrant[] grants)
        {
            return new UserAccess { TenantId = 7, UserId = 42, ComputedAt = Now, Grants = grants };
        }

        /// <summary>A problem of a stored permission.</summary>
        /// <param name="permissionId">The permission.</param>
        /// <param name="resource">Its resource.</param>
        /// <param name="action">Its action.</param>
        /// <param name="code">Why it grants nothing.</param>
        /// <returns>The problem.</returns>
        public static AccessProblem Problem(int permissionId, string resource, string action, AccessProblemCode code)
        {
            return new AccessProblem(permissionId, 10, resource, action, code, []);
        }

        /// <summary>The texts and stamps of a filtered decision's leaves, which must all be leaves.</summary>
        /// <param name="decision">The decision.</param>
        /// <returns>Each leaf's text and stamp, in order.</returns>
        public static List<(string Text, int? Version)> Leaves(AccessDecision decision)
        {
            FilterTree.OrNode or = Assert.IsType<FilterTree.OrNode>(decision.Filter);
            return [.. or.Children.Select(static child => Assert.IsType<FilterTree.LeafNode>(child)).Select(static leaf => (leaf.Text, leaf.QueryexVersion))];
        }

        /// <summary>Compiles a query of the widgets' ids under a filter, failing the test on any diagnostic.</summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The SQL.</returns>
        public static string CompileWidgets(FilterTree filter)
        {
            return Compile(Widget, filter);
        }

        /// <summary>Compiles a query of an entity's ids under a filter, failing the test on any diagnostic.</summary>
        /// <param name="root">The entity queried.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>The SQL.</returns>
        public static string Compile(string root, FilterTree filter)
        {
            QueryexResult<CompiledQuery> result = new QueryexEngine().CompileQuery(
                new QuerySpec { Root = Schema.FindEntity(root)!, Select = "Id", Filter = filter },
                new QueryCompilationOptions { Schema = Schema, QueryexVersion = QueryexLanguage.Version, Limits = QueryexProfiles.PipelineLimits });

            Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(static diagnostic => diagnostic.Code)));
            return result.Value.Sql;
        }

        /// <summary>Builds the fixture's query schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema BuildSchema()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity(Kind, "[fx].[Kinds]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true);
            _ = builder.Entity(Widget, "[fx].[Widgets]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true)
                .Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true)
                .Property("OwnerId", QueryexType.QxNumeric, "OwnerId", isNotNull: true)
                .Property("KindId", QueryexType.QxNumeric, "KindId", isNotNull: true)
                .Navigation("Kind", Kind, "KindId");
            _ = builder.Entity(Book, "[fx].[Books]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Code", QueryexType.QxString, "Code", isNotNull: true);
            _ = builder.Entity(Header, "[fx].[Headers]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("BookId", QueryexType.QxNumeric, "BookId", isNotNull: true)
                .Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true)
                .Navigation("Book", Book, "BookId");
            _ = builder.Entity(Line, "[fx].[Lines]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("HeaderId", QueryexType.QxNumeric, "HeaderId", isNotNull: true)
                .Property("Quantity", QueryexType.QxNumeric, "Quantity", isNotNull: true)
                .Navigation("Header", Header, "HeaderId");
            return builder.Build();
        }
    }
}
