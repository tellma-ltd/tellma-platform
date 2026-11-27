// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.1-§12.2: what a background export or import stores in its job's arguments: the
    // typed request and the negotiated localization of the request that started it, so the job
    // renders its workbook, its labels and its messages exactly as the request would have.
    /// <summary>
    ///     The localization of the request that started a background export or import: its culture,
    ///     calendar, display zone and message language, as negotiated.
    /// </summary>
    /// <param name="Culture">The formatting culture, a language and a region (<c>ar-SA</c>).</param>
    /// <param name="Calendar">The code of the calendar dates render in.</param>
    /// <param name="TimeZone">The display zone, an IANA name.</param>
    /// <param name="Language">The language messages are written in.</param>
    public sealed record ExcelJobCulture(string Culture, string Calendar, string TimeZone, string Language)
    {
        /// <summary>The localization a request context negotiated.</summary>
        /// <param name="context">The request's context.</param>
        /// <returns>The culture.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
        public static ExcelJobCulture Of(RequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return new ExcelJobCulture(context.Culture, context.Calendar, context.TimeZone, context.Language);
        }
    }

    /// <summary>
    ///     The arguments of a background export job: the securable resource it reads, the
    ///     localization it renders in, and, in each case, the request of its shape. Serialized with a
    ///     <c>kind</c> member naming the case.
    /// </summary>
    /// <param name="Resource">The exported stack's or child surface's resource (<c>gl.Center</c>).</param>
    /// <param name="Culture">The localization of the request that started the export.</param>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = KindProperty, UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
    [JsonDerivedType(typeof(DisplayExportJobArguments), nameof(ExportKind.Display))]
    [JsonDerivedType(typeof(ForImportExportJobArguments), nameof(ExportKind.ForImport))]
    public abstract record ExportJobArguments(string Resource, ExcelJobCulture Culture)
    {
        /// <summary>The member that names the case.</summary>
        public const string KindProperty = "kind";

        /// <summary>The export's shape.</summary>
        [JsonIgnore]
        public abstract ExportKind Kind { get; }
    }

    /// <summary>The arguments of a display-shape background export.</summary>
    /// <param name="Resource">The exported stack's or child surface's resource.</param>
    /// <param name="Culture">The localization of the request that started the export.</param>
    /// <param name="Request">The export's request, as <c>export/start</c> received it.</param>
    public sealed record DisplayExportJobArguments(string Resource, ExcelJobCulture Culture, ExportRequest Request)
        : ExportJobArguments(Resource, Culture)
    {
        /// <inheritdoc />
        [JsonIgnore]
        public override ExportKind Kind => ExportKind.Display;
    }

    /// <summary>The arguments of an editable-shape background export.</summary>
    /// <param name="Resource">The exported stack's resource.</param>
    /// <param name="Culture">The localization of the request that started the export.</param>
    /// <param name="Request">The export's request, as <c>export-for-import/start</c> received it.</param>
    public sealed record ForImportExportJobArguments(string Resource, ExcelJobCulture Culture, ExportForImportRequest Request)
        : ExportJobArguments(Resource, Culture)
    {
        /// <inheritdoc />
        [JsonIgnore]
        public override ExportKind Kind => ExportKind.ForImport;
    }

    /// <summary>The arguments of a background import job.</summary>
    /// <param name="Resource">The imported stack's resource.</param>
    /// <param name="Culture">The localization of the request that started the import.</param>
    /// <param name="Request">The import's request, as <c>import/start</c> received it.</param>
    public sealed record ImportJobArguments(string Resource, ExcelJobCulture Culture, StartImportRequest Request);
}
