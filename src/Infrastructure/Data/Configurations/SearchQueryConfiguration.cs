using EZPrice.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EZPrice.Infrastructure.Data.Configurations;

public class SearchQueryConfiguration : IEntityTypeConfiguration<SearchQuery>
{
    public void Configure(EntityTypeBuilder<SearchQuery> builder)
    {
        builder.Property(q => q.Query)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(q => q.QueryKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(q => q.QueryKey).IsUnique();
    }
}
