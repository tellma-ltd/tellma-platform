// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tellma.Core.EntityFrameworkCore.LockEscalation
{
    /// <summary>
    ///     Declares and reads a table's <c>LOCK_ESCALATION</c> option. The migrations of a context that
    ///     calls <c>UseTableTypes()</c> emit <c>ALTER TABLE ... SET (LOCK_ESCALATION = ...)</c> when
    ///     they create the table and whenever the declared option changes.
    /// </summary>
    public static class LockEscalationExtensions
    {
        /// <summary>Declares the lock escalation option of the entity's table.</summary>
        /// <param name="entityTypeBuilder">The entity type's builder.</param>
        /// <param name="mode">The option.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode" /> is not a defined option.</exception>
        public static EntityTypeBuilder HasLockEscalation(this EntityTypeBuilder entityTypeBuilder, LockEscalationMode mode)
        {
            ArgumentNullException.ThrowIfNull(entityTypeBuilder);
            _ = entityTypeBuilder.HasAnnotation(LockEscalationAnnotationNames.LockEscalation, Keyword(mode));
            return entityTypeBuilder;
        }

        /// <inheritdoc cref="HasLockEscalation(EntityTypeBuilder, LockEscalationMode)" />
        /// <typeparam name="TEntity">The entity type.</typeparam>
        public static EntityTypeBuilder<TEntity> HasLockEscalation<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder, LockEscalationMode mode)
            where TEntity : class
        {
            return (EntityTypeBuilder<TEntity>)HasLockEscalation((EntityTypeBuilder)entityTypeBuilder, mode);
        }

        /// <summary>The lock escalation option the entity's table declares, or null when it declares none (SQL Server's <c>TABLE</c>).</summary>
        /// <param name="entityType">The entity type.</param>
        /// <returns>The option's SQL keyword, or null.</returns>
        public static string? GetLockEscalation(this IReadOnlyEntityType entityType)
        {
            ArgumentNullException.ThrowIfNull(entityType);
            return entityType.FindAnnotation(LockEscalationAnnotationNames.LockEscalation)?.Value as string;
        }

        /// <summary>The SQL keyword of an option.</summary>
        /// <param name="mode">The option.</param>
        /// <returns><c>TABLE</c>, <c>AUTO</c> or <c>DISABLE</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode" /> is not a defined option.</exception>
        public static string Keyword(LockEscalationMode mode)
        {
            return mode switch
            {
                LockEscalationMode.Table => "TABLE",
                LockEscalationMode.Auto => "AUTO",
                LockEscalationMode.Disable => "DISABLE",
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a lock escalation option."),
            };
        }
    }
}
