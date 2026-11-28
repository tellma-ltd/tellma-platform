// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Declares, on an entity type, the operations its stack serves and the ceilings that bound
    ///     them. Optional: a stack without it serves <see cref="StackOperations.All" /> under the
    ///     platform defaults below. Inherited, so a distribution's leaf keeps its pack default's
    ///     declaration unless it declares its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every ceiling binds every caller alike: the web surface, the agent tools, import and
    ///         background work. A surface may only lower a ceiling for itself. A request beyond a
    ///         ceiling is refused with a limit-exceeded error, as early as the ceiling can be checked,
    ///         and never silently clamped, because clamping breaks paging arithmetic; the count cap
    ///         alone bounds an answer rather than a request. Each ceiling is a positive number.
    ///     </para>
    ///     <para>
    ///         The query default select is declared with
    ///         <see cref="Api.DefaultSelectAttribute" />, the related projection with
    ///         <see cref="Api.RelatedSelectAttribute" /> and the details navigations with
    ///         <see cref="Api.DetailsExpandAttribute" />.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>[Stack(Operations = StackOperations.Query | StackOperations.Details, DefaultOrderBy = "AssignedAt desc, Id desc")]</code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class StackAttribute : Attribute
    {
        /// <summary>The operations the stack serves; <see cref="StackOperations.All" /> by default.</summary>
        public StackOperations Operations { get; set; } = StackOperations.All;

        /// <summary>The most rows one query page or one get by parent ids returns; 10,000 by default.</summary>
        public int MaxTake { get; set; } = 10_000;

        /// <summary>The cap on a query's grand-total count; 10,000 by default. A count above it reports the cap plus one.</summary>
        public int CountCap { get; set; } = 10_000;

        /// <summary>The most a query's skip plus its page size may reach; 100,000 by default.</summary>
        public int MaxSkipWindow { get; set; } = 100_000;

        /// <summary>The most entities one save carries; 10,000 by default.</summary>
        public int MaxSaveCount { get; set; } = 10_000;

        /// <summary>The most rows one save carries, counted at every depth; 100,000 by default.</summary>
        public int MaxRowsPerSave { get; set; } = 100_000;

        /// <summary>The most ids one request names (get by ids, delete, an action, get by parent ids); 10,000 by default.</summary>
        public int MaxIds { get; set; } = 10_000;

        /// <summary>The most rows one delete by query may match; 10,000 by default.</summary>
        public int MaxDeleteByQueryRows { get; set; } = 10_000;

        /// <summary>The deepest navigation the details projection follows; 3 by default.</summary>
        public int MaxExpandDepth { get; set; } = 3;

        /// <summary>The most validation rounds one save, delete or action dispatches; 3 by default.</summary>
        public int MaxValidationRounds { get; set; } = 3;

        /// <summary>The longest search text a query accepts, in characters; 200 by default.</summary>
        public int MaxSearchLength { get; set; } = 200;

        /// <summary>The most details extras one request names; 16 by default.</summary>
        public int MaxExtras { get; set; } = 16;

        /// <summary>
        ///     The Queryex ordering an operation uses when the request names none, such as
        ///     <c>"AssignedAt desc, Id desc"</c>; null leaves each operation its own default.
        /// </summary>
        public string? DefaultOrderBy { get; set; }
    }
}
