// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Tests.Caching
{
    // The entities the version-tag registry is built from: a sound set (plain tags, a table bumping two
    // tags, a cacheable list, user-level rules by the default and by another property), and one entity
    // per declaration the registry refuses.

    /// <summary>Bumps the plain tag <c>currencies</c>.</summary>
    [Table("Currencies", Schema = "vt")]
    [BumpsVersionTag("currencies")]
    public class VtCurrency : Entity<int>
    {
        /// <summary>The code.</summary>
        [MaxLength(3)]
        public string Code { get; set; } = null!;
    }

    /// <summary>Bumps two plain tags, one of them shared with <see cref="VtCurrency" />.</summary>
    [Table("Units", Schema = "vt")]
    [BumpsVersionTag("units")]
    [BumpsVersionTag("currencies")]
    public class VtUnit : Entity<int>
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A cacheable list, whose tag is implied.</summary>
    [Table("Lists", Schema = "vt")]
    [Cacheable]
    [TableType]
    public class VtList : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }

    /// <summary>Bumps the preferences tag of the user its <c>UserId</c> names.</summary>
    [Table("Profiles", Schema = "vt")]
    [BumpsUserVersionTag(UserVersionTagNames.Preferences)]
    public class VtProfile : Entity<int>
    {
        /// <summary>The user.</summary>
        public int UserId { get; set; }
    }

    /// <summary>Bumps the permissions tag of the user a nullable property of another name names.</summary>
    [Table("Grants", Schema = "vt")]
    [BumpsUserVersionTag(UserVersionTagNames.Permissions, nameof(GranteeId))]
    public class VtGrant : Entity<int>
    {
        /// <summary>The user the grant is for.</summary>
        public int? GranteeId { get; set; }
    }

    /// <summary>Declares an entity tag by hand.</summary>
    [Table("HandNamed", Schema = "vt")]
    [BumpsVersionTag("entity:vt.VtList")]
    public class VtHandNamed : Entity<int>;

    /// <summary>Declares a tag with an upper-case letter.</summary>
    [Table("UpperNamed", Schema = "vt")]
    [BumpsVersionTag("Currencies")]
    public class VtUpperNamed : Entity<int>;

    /// <summary>Declares a tag that starts with a digit.</summary>
    [Table("DigitNamed", Schema = "vt")]
    [BumpsVersionTag("9lives")]
    public class VtDigitNamed : Entity<int>;

    /// <summary>Names a user-id property it does not have.</summary>
    [Table("Ownerless", Schema = "vt")]
    [BumpsUserVersionTag(UserVersionTagNames.Preferences, "Owner")]
    public class VtOwnerless : Entity<int>;

    /// <summary>Names a string property as the user id.</summary>
    [Table("Labelled", Schema = "vt")]
    [BumpsUserVersionTag(UserVersionTagNames.Preferences, nameof(Label))]
    public class VtLabelled : Entity<int>
    {
        /// <summary>A label, not a user id.</summary>
        [MaxLength(50)]
        public string Label { get; set; } = null!;
    }

    /// <summary>Names a user-level tag that does not exist.</summary>
    [Table("Unknown", Schema = "vt")]
    [BumpsUserVersionTag((UserVersionTagNames)7)]
    public class VtUnknownUserTag : Entity<int>
    {
        /// <summary>The user.</summary>
        public int UserId { get; set; }
    }

    /// <summary>A cacheable list whose name differs from <see cref="VtList" />'s in case alone.</summary>
    [Table("ListsAgain", Schema = "vt")]
    [Cacheable]
    [TableType]
    public class VTLIST : TopLevelEntity;

    /// <summary>A cacheable list whose implied tag is longer than the column holds.</summary>
    [Table("Long", Schema = "vt")]
    [Cacheable]
    [TableType]
    public class VtAListWhoseNameIsSoLongThatItsImpliedVersionTagNameCannotFitTheColumnThatStoresTheTagsOfEveryTenantDatabaseAtAllEvenNow : TopLevelEntity;
}
