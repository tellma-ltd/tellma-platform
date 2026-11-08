// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     The platform's validation codes: the dotted PascalCase resource keys the pipeline's own
    ///     rules raise. Other Core features name their own families (<c>Users.NotService</c>), and a
    ///     pack or distribution names <c>&lt;Schema&gt;.&lt;Area&gt;.&lt;Rule&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     Codes are stable: a platform minor may add one, never rename one. Each code's message is
    ///     the resource of the same key, rendered from the arguments listed with it.
    /// </remarks>
    public static class ValidationCodes
    {
        /// <summary>A required value is missing. Argument: <c>property</c>.</summary>
        public const string Required = "Required";

        /// <summary>A string is longer than its column allows. Arguments: <c>property</c>, <c>maxLength</c>.</summary>
        public const string MaxLength = "MaxLength";

        /// <summary>A value lies outside its declared range.</summary>
        public const string Range = "Range";

        /// <summary>A value that must be unique is already taken, in the payload or in the database.</summary>
        public const string Unique = "Unique";

        /// <summary>An update changes a value that may be set only once.</summary>
        public const string WriteOnce = "WriteOnce";

        /// <summary>
        ///     A decimal has more digits than its column's precision or scale allows; the server never
        ///     rounds it. Arguments: <c>property</c>, <c>precision</c>, <c>scale</c>.
        /// </summary>
        public const string Precision = "Precision";

        /// <summary>A value lies outside its declared value set. Arguments: <c>set</c>, <c>value</c>.</summary>
        public const string ValueNotInSet = "Validation.ValueNotInSet";

        /// <summary>A row the operation names does not exist or is not visible to the caller.</summary>
        public const string EntityNotFound = "Entity.NotFound";

        /// <summary>Two payload rows carry the same positive or temporary id.</summary>
        public const string EntityDuplicateId = "Entity.DuplicateId";

        /// <summary>
        ///     A reference names a row that does not exist or is not visible to the caller. Arguments:
        ///     <c>entity</c> (the target's entity name), <c>id</c>.
        /// </summary>
        public const string FkNotFound = "Fk.NotFound";

        /// <summary>
        ///     A delete is refused because another row references the row. Arguments: <c>entity</c>
        ///     (the referencing entity's name), <c>property</c>.
        /// </summary>
        public const string FkInUse = "Fk.InUse";

        /// <summary>A tree row would become its own ancestor.</summary>
        public const string TreeCycle = "Tree.Cycle";

        /// <summary>A tree would grow deeper than its limit.</summary>
        public const string TreeTooDeep = "Tree.TooDeep";

        /// <summary>A blob reference names a blob that cannot be attached. Argument: <c>reason</c>.</summary>
        public const string BlobNotAttachable = "Blob.NotAttachable";

        /// <summary>One staged blob is referenced twice in a save.</summary>
        public const string BlobDuplicateReference = "Blob.DuplicateReference";

        /// <summary>An update under the <c>Check</c> concurrency mode carries no stamp.</summary>
        public const string ConcurrencyStampRequired = "Concurrency.StampRequired";

        /// <summary>An import writes a language the tenant has not configured.</summary>
        public const string ImportLanguageNotConfigured = "Import.LanguageNotConfigured";

        /// <summary>
        ///     A query's select item has the Geography type, which has no JSON encoding; filters,
        ///     orderings and having may still use it. A query diagnostic. Argument: <c>item</c>.
        /// </summary>
        public const string QueryGeographyNotSelectable = "Query.GeographyNotSelectable";

        /// <summary>
        ///     A path a query names cannot be reached by the caller: the discovery form of a traversal
        ///     refusal. Arguments: <c>entity</c>, <c>path</c>.
        /// </summary>
        public const string QueryPathNotReachable = "Query.PathNotReachable";

        /// <summary>A query names a named filter the stack does not declare. Argument: <c>name</c>.</summary>
        public const string QueryUnknownNamedFilter = "Query.UnknownNamedFilter";

        /// <summary>
        ///     A request's query text divides by zero or overflows when it runs. Argument:
        ///     <c>reason</c>.
        /// </summary>
        public const string QueryArithmeticFailed = "Query.ArithmeticFailed";

        /// <summary>
        ///     A display-name prefix value holds the separator that joins it to the name. Arguments:
        ///     <c>property</c>, <c>separator</c>.
        /// </summary>
        public const string DisplayNamePrefixSeparator = "DisplayName.PrefixSeparator";
    }
}
