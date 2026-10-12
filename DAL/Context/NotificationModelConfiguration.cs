using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

public static class NotificationModelConfiguration
{
    public static void ConfigureNotificationInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationProcessingMarker>(marker =>
        {
            marker.ToTable("NotificationProcessingMarkers", table =>
                table.HasCheckConstraint("CK_NotificationProcessingMarkers_Disposition", "[Disposition] IN (0, 1)"));
            marker.HasKey(value => value.MessageId);
            marker.Property(value => value.MessageId).ValueGeneratedNever();
            marker.Property(value => value.CanonicalPayloadHash).HasMaxLength(64).IsRequired();
            marker.HasIndex(value => value.ProcessedAtUtc);
        });
        modelBuilder.Entity<UserNotification>(notification =>
        {
            notification.ToTable("UserNotifications", table =>
                table.HasCheckConstraint("CK_UserNotifications_Kind", "[Kind] IN (0, 1, 2, 3)"));
            notification.HasKey(value => value.Id);
            notification.Property(value => value.Id).ValueGeneratedNever();
            notification.Property(value => value.TaskName).HasMaxLength(100).IsRequired();
            notification.Property(value => value.ProjectName).HasMaxLength(100).IsRequired();
            notification.HasOne(value => value.Owner).WithMany().HasForeignKey(value => value.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            notification.HasOne<NotificationProcessingMarker>().WithMany().HasForeignKey(value => value.Id)
                .OnDelete(DeleteBehavior.Restrict);
            // Task/project IDs are historical scalars. Deleting either may hide
            // the row, but must not erase the marker and resurrect a replay.
            notification.HasIndex(value => new { value.OwnerId, value.OccurredAtUtc, value.Id });
            notification.HasIndex(value => new { value.OwnerId, value.ReadAtUtc });
        });
    }
}
