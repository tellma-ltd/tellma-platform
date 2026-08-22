// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Queryex.Testing.Schema
{
    /// <summary>
    ///     The schema every Queryex suite binds against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately awkward. Logical and physical names differ on an entity, on properties,
    ///         and on a navigation, so golden SQL has to show the physical name and no test can pass
    ///         because the two happened to match. A nullable navigation stands in front of a
    ///         non-nullable one, so the rule that a join behind an optional one stays optional is
    ///         observable rather than assumed. There is a non-Unicode column beside the Unicode
    ///         ones and a sized decimal, so how a parameter slot is typed is visible. A hierarchical
    ///         entity carries both a unique property and a non-unique one. And two properties are
    ///         named after things the language uses — a function and a reserved word — because the
    ///         rule that neither is reserved is easy to state and easy to break.
    ///     </para>
    ///     <para>
    ///         Every type in the language has both a column that may be absent and one that may not.
    ///         The first is what lets the totality of a function be checked against real absent data
    ///         rather than against a written absent value, which would fold away before the emission
    ///         it is meant to exercise ever ran.
    ///     </para>
    /// </remarks>
    public static class LedgerFixture
    {
        /// <summary>The version this fixture presents itself under.</summary>
        public const string Version = "ledger-1";

        /// <summary>The schema, built once.</summary>
        public static QueryexSchema Schema { get; } = Build();

        /// <summary>The entity most suites query from.</summary>
        public static EntityDescriptor Invoice { get; } = Schema.FindEntity("Invoice")!;

        /// <summary>The hierarchical entity.</summary>
        public static EntityDescriptor Account { get; } = Schema.FindEntity("Account")!;

        /// <summary>An entity that is not hierarchical, so asking it for ancestry fails.</summary>
        public static EntityDescriptor Party { get; } = Schema.FindEntity("Party")!;

        /// <summary>Builds the schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema Build()
        {
            QueryexSchemaBuilder builder = new(Version);

            // The entity name, the key's name, and several columns all differ from what an
            // expression author writes.
            QueryexSchemaBuilder.EntityBuilder invoice = builder.Entity("Invoice", "[gl].[Documents]");
            invoice.Key("Id", QueryexType.QxNumeric, "DocumentId", QueryexStoreType.QxInt);
            invoice.Property("PostingDate", QueryexType.QxDate, "PostingDate", isNotNull: true, storeType: QueryexStoreType.QxDate);
            invoice.Property("DueDate", QueryexType.QxDate, "DueDate", storeType: QueryexStoreType.QxDate);
            invoice.Property("PostedAt", QueryexType.QxDateTimeOffset, "PostedAt", storeType: QueryexStoreType.QxDateTimeOffset(7));
            invoice.Property("ApprovedAt", QueryexType.QxDateTimeOffset, "ApprovedAt", isNotNull: true, storeType: QueryexStoreType.QxDateTimeOffset(7));
            invoice.Property("DueOn", QueryexType.QxDateTime, "DueOn", storeType: QueryexStoreType.QxDateTime2(7));
            invoice.Property("PostedOn", QueryexType.QxDateTime, "PostedOn", isNotNull: true, storeType: QueryexStoreType.QxDateTime2(7));
            invoice.Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true, storeType: QueryexStoreType.QxDecimal(19, 4));
            invoice.Property("Rate", QueryexType.QxNumeric, "Rate", storeType: QueryexStoreType.QxDecimal(9, 6));
            invoice.Property("Memo", QueryexType.QxString, "Memo", storeType: QueryexStoreType.QxNVarChar(255));

            // Non-Unicode and unique, so a literal compared against it shows family-only slot typing.
            invoice.Property("Code", QueryexType.QxString, "DocCode", isNotNull: true, isUnique: true, storeType: QueryexStoreType.QxVarChar(50));

            // Fixed width, so data shorter than the column is padded in storage.
            invoice.Property("Ref", QueryexType.QxString, "DocRef", storeType: QueryexStoreType.QxChar(10));
            invoice.Property("Notes", QueryexType.QxString, "Notes", storeType: QueryexStoreType.QxNVarChar(null));
            invoice.Property("IsPosted", QueryexType.QxBool, "IsPosted", isNotNull: true, storeType: QueryexStoreType.QxBit);
            invoice.Property("IsApproved", QueryexType.QxBool, "IsApproved", storeType: QueryexStoreType.QxBit);
            invoice.Property("Gender", QueryexType.QxString, "Gender", storeType: QueryexStoreType.QxNChar(1));
            invoice.Property("ExternalId", QueryexType.QxGuid, "ExternalId", storeType: QueryexStoreType.QxUniqueIdentifier);
            invoice.Property("BatchId", QueryexType.QxGuid, "BatchId", isNotNull: true, storeType: QueryexStoreType.QxUniqueIdentifier);

            // Named after a function, which is not reserved: it is a call only when a parenthesis
            // follows it.
            invoice.Property("Count", QueryexType.QxNumeric, "LineCount", isNotNull: true, storeType: QueryexStoreType.QxInt);

            // Named after a reserved word, so it can only be written in brackets.
            invoice.Property("not", QueryexType.QxBool, "NotFlag", isNotNull: true, storeType: QueryexStoreType.QxBit);
            invoice.Property("Location", QueryexType.QxGeography, "Location", storeType: QueryexStoreType.QxGeography);
            invoice.Property("Territory", QueryexType.QxGeography, "Territory", isNotNull: true, storeType: QueryexStoreType.QxGeography);
            invoice.Property("AltNode", QueryexType.QxHierarchyId, "AltNode", storeType: QueryexStoreType.QxHierarchyId);
            invoice.Property("CreatedById", QueryexType.QxNumeric, "CreatedById", isNotNull: true, storeType: QueryexStoreType.QxInt);

            // Optional, so everything reached through it may be absent and its join stays optional.
            invoice.Property("CustomerId", QueryexType.QxNumeric, "AgentFk", storeType: QueryexStoreType.QxInt);
            invoice.Property("CentreId", QueryexType.QxNumeric, "SegmentId", isNotNull: true, storeType: QueryexStoreType.QxInt);
            invoice.Property("AccountId", QueryexType.QxNumeric, "AccountId", isNotNull: true, storeType: QueryexStoreType.QxInt);
            invoice.Navigation("Customer", "Party", "CustomerId");
            invoice.Navigation("Centre", "Centre", "CentreId");
            invoice.Navigation("Account", "Account", "AccountId");

            QueryexSchemaBuilder.EntityBuilder party = builder.Entity("Party", "[dbo].[Agents]");
            party.Key("Id", QueryexType.QxNumeric, "AgentId", QueryexStoreType.QxInt);
            party.Property("Name", QueryexType.QxString, "AgentName", isNotNull: true, storeType: QueryexStoreType.QxNVarChar(255));
            party.Property("Gender", QueryexType.QxString, "Gender", storeType: QueryexStoreType.QxNChar(1));
            party.Property("TaxId", QueryexType.QxString, "TaxNumber", storeType: QueryexStoreType.QxVarChar(20));
            party.Property("RegionId", QueryexType.QxNumeric, "RegionId", isNotNull: true, storeType: QueryexStoreType.QxInt);
            party.Property("ManagerId", QueryexType.QxNumeric, "ManagerId", storeType: QueryexStoreType.QxInt);

            // Non-nullable, and therefore an inner join — except behind the optional customer, where
            // it has to stay an outer one.
            party.Navigation("Region", "Region", "RegionId");
            party.Navigation("Manager", "Party", "ManagerId");

            QueryexSchemaBuilder.EntityBuilder centre = builder.Entity("Centre", "[gl].[Segments]");
            centre.Key("Id", QueryexType.QxNumeric, "SegmentId", QueryexStoreType.QxInt);
            centre.Property("Name", QueryexType.QxString, "SegmentName", isNotNull: true, storeType: QueryexStoreType.QxNVarChar(100));
            centre.Property("RegionId", QueryexType.QxNumeric, "RegionId", isNotNull: true, storeType: QueryexStoreType.QxInt);
            centre.Navigation("Region", "Region", "RegionId");

            QueryexSchemaBuilder.EntityBuilder region = builder.Entity("Region", "[dbo].[Regions]");
            region.Key("Id", QueryexType.QxNumeric, "Id", QueryexStoreType.QxInt);
            region.Property("Name", QueryexType.QxString, "Name", isNotNull: true, storeType: QueryexStoreType.QxNVarChar(100));
            region.Property("Code", QueryexType.QxString, "Code", isNotNull: true, isUnique: true, storeType: QueryexStoreType.QxChar(10));

            QueryexSchemaBuilder.EntityBuilder account = builder.Entity("Account", "[gl].[Accounts]");
            account.Key("Id", QueryexType.QxNumeric, "AccountId", QueryexStoreType.QxInt);
            account.Property("Concept", QueryexType.QxString, "Concept", isNotNull: true, isUnique: true, storeType: QueryexStoreType.QxVarChar(100));

            // Not unique, so asking for ancestry by it has to be refused: a key that named several
            // rows would make the lookup pick one of them arbitrarily.
            account.Property("Label", QueryexType.QxString, "Label", isNotNull: true, storeType: QueryexStoreType.QxNVarChar(255));
            account.Property("Node", QueryexType.QxHierarchyId, "TreeNode", isNotNull: true, storeType: QueryexStoreType.QxHierarchyId);
            account.Property("ParentId", QueryexType.QxNumeric, "ParentId", storeType: QueryexStoreType.QxInt);
            account.TreeNode("Node");

            // A cycle, which the two-phase build exists to handle.
            account.Navigation("Parent", "Account", "ParentId");

            return builder.Build();
        }
    }
}
