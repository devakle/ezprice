using EZPrice.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EZPrice.Infrastructure.Data.Configurations;

public class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.Property(o => o.Query)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(o => o.QueryKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(o => o.Source)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.Title)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(o => o.Currency)
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(o => o.Url)
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(o => o.ImageUrl)
            .HasMaxLength(2048);

        builder.HasIndex(o => new { o.QueryKey, o.Source, o.Page });
    }
}
