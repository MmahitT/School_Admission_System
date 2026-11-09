using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Infrastructure.Persistence.Configurations;

public class AdmissionClassConfiguration : IEntityTypeConfiguration<AdmissionClass>
{
    public void Configure(EntityTypeBuilder<AdmissionClass> b)
    {
        b.ToTable("admission_classes", t =>
            t.HasCheckConstraint("ck_admission_classes_total_seats_positive", "total_seats > 0"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.TotalSeats).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.UpdatedAt).IsRequired();

        // Case-insensitive unique name (lower(name)) is created in raw SQL inside the migration.
    }
}
