using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

internal static class AiDraftModelConfiguration
{
    public static void ConfigureAiDrafts(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiTaskDraft>(draft =>
        {
            draft.ToTable("AiTaskDrafts");
            draft.HasKey(value => value.Id);
            draft.Property(value => value.Id).ValueGeneratedNever();
            draft.Property(value => value.Version).IsConcurrencyToken();
            draft.Property(value => value.NotesText).HasMaxLength(20_000);
            draft.Property(value => value.Locale).HasMaxLength(8).IsRequired();
            draft.Property(value => value.TimeZoneId).HasMaxLength(128).IsRequired();
            draft.HasIndex(value => new { value.OwnerId, value.CreatedAtUtc });
            draft.HasIndex(value => value.ExpiresAtUtc);
            draft.HasIndex(value => value.RetainUntilUtc);
            draft.HasOne(value => value.Owner).WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Cascade);
            draft.HasOne(value => value.Project).WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AiTaskDraftItem>(item =>
        {
            item.ToTable("AiTaskDraftItems");
            item.HasKey(value => value.Id);
            item.Property(value => value.Id).ValueGeneratedNever();
            item.Property(value => value.CandidateJson).HasMaxLength(131_072);
            item.HasIndex(value => new { value.DraftId, value.Ordinal }).IsUnique();
            item.HasOne(value => value.Draft).WithMany(value => value.Items).HasForeignKey(value => value.DraftId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AiTaskMaterialization>(link =>
        {
            link.ToTable("AiTaskMaterializations");
            link.HasKey(value => value.DraftItemId);
            link.Property(value => value.DraftItemId).ValueGeneratedNever();
            link.HasIndex(value => value.OutboxMessageId).IsUnique();
            link.HasOne(value => value.DraftItem).WithOne(value => value.Materialization).HasForeignKey<AiTaskMaterialization>(value => value.DraftItemId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne(value => value.LiveTask).WithMany().HasForeignKey(value => value.LiveTaskId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<AiTaskCommitReceipt>(receipt =>
        {
            receipt.ToTable("AiTaskCommitReceipts");
            receipt.HasKey(value => value.Id);
            receipt.Property(value => value.Id).ValueGeneratedNever();
            receipt.Property(value => value.KeyHash).HasMaxLength(64).IsRequired();
            receipt.Property(value => value.PayloadHash).HasMaxLength(64).IsRequired();
            receipt.Property(value => value.ResponseJson).HasMaxLength(65_536).IsRequired();
            receipt.HasIndex(value => new { value.OwnerId, value.KeyHash }).IsUnique();
            receipt.HasIndex(value => value.ReplayExpiresAtUtc);
            receipt.HasOne(value => value.Owner).WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AiDailyUsage>(usage =>
        {
            usage.ToTable("AiDailyUsage");
            usage.HasKey(value => new { value.ScopeKey, value.DayUtc });
            usage.Property(value => value.ScopeKey).HasMaxLength(64);
            usage.Property(value => value.Version).IsConcurrencyToken();
        });
    }
}
