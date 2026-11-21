// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The vocabulary of the hidden <c>_tellma</c> manifest sheet: the keys of its header block,
    ///     the headers of its column table, and the header block of an entity export.
    /// </summary>
    /// <remarks>
    ///     The header block fills column A (keys) and B (values) from row 1; one blank row follows,
    ///     then the column table's header row, then one row per data column of every data sheet.
    /// </remarks>
    internal static class ExcelManifest
    {
        /// <summary>The manifest format this codec writes.</summary>
        public const int Version = 1;

        /// <summary>Key: the manifest format version; the first row.</summary>
        public const string VersionKey = "Tellma.Manifest";

        /// <summary>Key: the export's shape, a member name of the export kinds.</summary>
        public const string ShapeKey = "Shape";

        /// <summary>Key: the exported entity's name.</summary>
        public const string EntityKey = "Entity";

        /// <summary>Key: the deployment the file was exported from.</summary>
        public const string DeploymentKey = "Deployment";

        /// <summary>Key: the platform version that wrote the file.</summary>
        public const string PlatformKey = "Platform";

        /// <summary>Key: the tenant the file was exported from.</summary>
        public const string SourceTenantIdKey = "SourceTenantId";

        /// <summary>Key: when the file was exported, ISO 8601 at offset zero.</summary>
        public const string ExportedAtKey = "ExportedAt";

        /// <summary>Key: the culture the file was written in.</summary>
        public const string CultureKey = "Culture";

        /// <summary>Key: the calendar the file's dates were written in.</summary>
        public const string CalendarKey = "Calendar";

        /// <summary>Key: the display zone the file's instants were written in.</summary>
        public const string TimeZoneKey = "TimeZone";

        /// <summary>Key: the tenant's content languages in slot order.</summary>
        public const string LanguagesKey = "Languages";

        /// <summary>Key: the entity's schema fingerprint.</summary>
        public const string SchemaFingerprintKey = "SchemaFingerprint";

        /// <summary>Key: reserved; always false, since an export fails rather than truncate.</summary>
        public const string TruncatedKey = "Truncated";

        /// <summary>The headers of the column table, in column order.</summary>
        public static readonly IReadOnlyList<string> ColumnTableHeaders =
        [
            "Sheet",
            "Collection",
            "Column",
            "Header",
            "Path",
            "Type",
            "Role",
            "Language",
            "ReferenceEntity",
            "KeyKind",
            "DateEncoding",
            "DatePattern",
        ];

        /// <summary>The header block of an entity export, in the order it is written.</summary>
        /// <param name="entity">The exported entity.</param>
        /// <param name="shape">The export's shape.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="platformVersion">The platform version writing the file.</param>
        /// <returns>The block, key to value.</returns>
        public static IReadOnlyDictionary<string, string> Header(
            EntityMetadata entity,
            ExportKind shape,
            ExcelContext context,
            string platformVersion)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(platformVersion);

            return new ReadOnlyDictionary<string, string>(new OrderedDictionary<string, string>(StringComparer.Ordinal)
            {
                [VersionKey] = Version.ToString(CultureInfo.InvariantCulture),
                [ShapeKey] = shape.ToString(),
                [EntityKey] = entity.Name,
                [DeploymentKey] = context.DeploymentId,
                [PlatformKey] = platformVersion,
                [SourceTenantIdKey] = context.TenantId.ToString(CultureInfo.InvariantCulture),
                [ExportedAtKey] = context.Now.ToUniversalTime().ToString(CellCodec.StampPattern, CultureInfo.InvariantCulture),
                [CultureKey] = context.Culture.Name,
                [CalendarKey] = context.Calendar.Code,
                [TimeZoneKey] = context.TimeZone.Id,
                [LanguagesKey] = string.Join(", ", context.TenantLanguages),
                [SchemaFingerprintKey] = entity.SchemaFingerprint,
                [TruncatedKey] = "false",
            });
        }

        /// <summary>A Queryex type's name as the manifest records it: without its <c>Qx</c> prefix.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name (<c>Numeric</c>).</returns>
        public static string TypeName(Queryex.QueryexType type)
        {
            string name = type.ToString();
            return name.StartsWith("Qx", StringComparison.Ordinal) ? name[2..] : name;
        }
    }
}
