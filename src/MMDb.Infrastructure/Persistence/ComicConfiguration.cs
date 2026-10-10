using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMDb.Domain;
using MMDb.Domain.Comics;

namespace MMDb.Infrastructure.Persistence;

internal sealed class ComicConfiguration : IEntityTypeConfiguration<Comic>
{
    public const string UpcField = "_upc";

    public void Configure(EntityTypeBuilder<Comic> comic)
    {
        comic.ToTable("Comics");
        comic.HasKey(c => c.Id);
        comic.Property(c => c.Id).ValueGeneratedNever();
        comic.Property(c => c.Version).IsConcurrencyToken();

        comic.Property(c => c.Format).HasConversion<string>().HasMaxLength(16);
        comic.Property(c => c.Series).HasMaxLength(ComicDetails.MaxTextLength);
        comic.Property(c => c.Number).HasMaxLength(ComicDetails.MaxNumberLength);
        comic.Property(c => c.Title).HasMaxLength(ComicDetails.MaxTextLength);
        comic.Property(c => c.Variant).HasMaxLength(ComicDetails.MaxShortTextLength);
        comic.Property(c => c.Publisher).HasMaxLength(ComicDetails.MaxShortTextLength);
        comic.Property(c => c.KeyNote).HasMaxLength(ComicDetails.MaxShortTextLength);
        comic.Property(c => c.CoverUrl).HasMaxLength(ComicDetails.MaxCoverUrlLength);
        comic.Property(c => c.Writers).HasConversion(JsonList.Converter<string>(), JsonList.Comparer<string>());
        comic.Property(c => c.Artists).HasConversion(JsonList.Converter<string>(), JsonList.Comparer<string>());
        comic.Property(c => c.Isbn).HasConversion(isbn => isbn!.Value, value => Isbn.Parse(value)).HasMaxLength(13);

        comic.Ignore(c => c.Barcode);
        comic.Ignore(c => c.Details);
        comic.Ignore(c => c.DisplayName);
        comic.Property<string?>(UpcField).HasColumnName("Upc").HasMaxLength(13);
        comic.Property<string?>("_upcSupplement").HasColumnName("UpcSupplement").HasMaxLength(5);

        comic.HasIndex(UpcField);
        comic.HasIndex(c => c.Isbn);
        comic.HasIndex(c => c.MetronId);
    }
}

internal sealed class ComicSeriesConfiguration : IEntityTypeConfiguration<ComicSeries>
{
    public void Configure(EntityTypeBuilder<ComicSeries> series)
    {
        series.ToTable("ComicSeries");
        series.HasKey(s => s.MetronId);
        series.Property(s => s.MetronId).ValueGeneratedNever();
        series.Property(s => s.Name).IsRequired().HasMaxLength(ComicDetails.MaxTextLength);
        series.Property(s => s.Publisher).HasMaxLength(ComicDetails.MaxShortTextLength);
        series.Property(s => s.IssueNumbers).HasConversion(JsonList.Converter<string>(), JsonList.Comparer<string>());
    }
}
