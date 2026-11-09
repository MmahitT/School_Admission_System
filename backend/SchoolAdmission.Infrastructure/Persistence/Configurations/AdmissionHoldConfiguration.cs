using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Infrastructure.Persistence.Configurations;

public class AdmissionHoldConfiguration : IEntityTypeConfiguration<AdmissionHold>
{
    // Stored as the upper-case names used by the API: HELD, CONFIRMED, EXPIRED, CANCELLED.
    private static readonly ValueConverter<HoldStatus, string> StatusConverter = new(
        v => v.ToString().ToUpperInvariant(),
        v => Enum.Parse<HoldStatus>(v, true));

    public void Configure(EntityTypeBuilder<AdmissionHold> b)
    {
        b.ToTable("admission_holds", t =>
        {
            t.HasCheckConstraint("ck_admission_holds_student_id_format", "student_id ~ '^[0-9]{17}$'");
            t.HasCheckConstraint("ck_admission_holds_status_valid",
                "status IN ('HELD','CONFIRMED','EXPIRED','CANCELLED')");
            t.HasCheckConstraint("ck_admission_holds_expires_after_created", "expires_at > created_at");
            t.HasCheckConstraint("ck_admission_holds_confirmed_at_matches_status",
                "(status = 'CONFIRMED') = (confirmed_at IS NOT NULL)");
            t.HasCheckConstraint("ck_admission_holds_cancelled_at_matches_status",
                "(status = 'CANCELLED') = (cancelled_at IS NOT NULL)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ClassId).IsRequired();
        b.Property(x => x.StudentId).HasColumnType("character(17)").IsRequired();
        b.Property(x => x.Status).HasConversion(StatusConverter).HasMaxLength(16).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.ExpiresAt).IsRequired();

        b.HasOne(x => x.Class)
            .WithMany(c => c.Holds)
            .HasForeignKey(x => x.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ClassId).HasDatabaseName("ix_admission_holds_class_id");
        // ix_admission_holds_student_id (plain lookup index) is created in raw SQL in the migration:
        // EF Core merges two indexes over the same column, which would drop it in favour of the filtered unique one.
        b.HasIndex(x => x.Status).HasDatabaseName("ix_admission_holds_status");
        b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_admission_holds_expires_at");

        // Seat counting per class and the expiration sweeper.
        b.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_admission_holds_class_status_expires");

        // Rule 3: at most one active HELD hold per student across all classes.
        b.HasIndex(x => x.StudentId)
            .IsUnique()
            .HasFilter("status = 'HELD'")
            .HasDatabaseName("ux_admission_holds_one_held_per_student");

        // Rule 4: one application per student per class; EXPIRED rows do not block re-applying (ASSUMPTIONS A6).
        b.HasIndex(x => new { x.ClassId, x.StudentId })
            .IsUnique()
            .HasFilter("status IN ('HELD','CONFIRMED','CANCELLED')")
            .HasDatabaseName("ux_admission_holds_one_application_per_class");
    }
}
