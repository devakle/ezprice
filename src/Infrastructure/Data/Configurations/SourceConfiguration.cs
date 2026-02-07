using EZPrice.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EZPrice.Infrastructure.Data.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.Property(s => s.Name)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(s => s.Name).IsUnique();
    }
}
