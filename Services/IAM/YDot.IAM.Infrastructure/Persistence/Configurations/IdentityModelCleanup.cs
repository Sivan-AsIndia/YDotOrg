using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace YDot.IAM.Infrastructure.Persistence.Configurations;

/// <summary>
/// Removes the relationships <c>IdentityDbContext.OnModelCreating</c> sets up before our own
/// configurations replace them.
///
/// THE PROBLEM THIS SOLVES. The base context wires each join table to its principals with
/// navigation-less foreign keys, for example:
///
/// <code>
/// b.HasOne&lt;TUser&gt;().WithMany().HasForeignKey(ur =&gt; ur.UserId).IsRequired();
/// </code>
///
/// When our configuration then declares the same link WITH navigation properties and a
/// Tenant-scoped composite key, EF does not recognise the two as the same relationship — a
/// relationship with no navigation and one with navigation are distinct to the model builder.
/// The result is two foreign keys on the same column pair, which showed up in the generated
/// migration as <c>fk_iam_user_roles_iam_users_user_id</c> sitting beside
/// <c>fk_iam_user_roles_iam_users_tenant_id_user_id</c>.
///
/// Both constraints are satisfied by valid data, so nothing breaks — but the redundant one
/// is a second index to maintain on every write, and more importantly it is the WEAKER of
/// the two. Leaving a plain UserId foreign key in place beside the composite invites somebody
/// to conclude that the user link is not Tenant-scoped after all.
///
/// So the navigation-less originals are removed and only the composite Tenant-scoped
/// relationships remain.
/// </summary>
internal static class IdentityModelCleanup
{
    /// <summary>
    /// Drops every foreign key on this entity that has no navigation property on either end.
    /// Those are exactly the ones the base Identity model created; anything we declare
    /// ourselves carries at least one navigation and is left alone.
    ///
    /// Call this at the TOP of Configure, before declaring the replacement relationships.
    /// </summary>
    internal static void RemoveBaseIdentityRelationships<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        var navigationLess = builder.Metadata.GetForeignKeys()
            .Where(foreignKey => foreignKey.DependentToPrincipal is null
                                 && foreignKey.PrincipalToDependent is null)
            .ToList();

        foreach (var foreignKey in navigationLess)
        {
            builder.Metadata.RemoveForeignKey(foreignKey);
        }
    }
}
