using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMDb.Domain;
using MMDb.Domain.Movies;

namespace MMDb.Infrastructure.Persistence;

internal sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> movie)
    {
        movie.ToTable("Movies");
        movie.HasKey(m => m.Id);
        movie.Property(m => m.Id).ValueGeneratedNever();
        movie.Property(m => m.Version).IsConcurrencyToken();

        movie.Property(m => m.Title).IsRequired().HasMaxLength(MovieDetails.MaxTitleLength);
        movie.Property(m => m.Rating).HasMaxLength(MovieDetails.MaxRatingLength);
        movie.Property(m => m.Directors).HasConversion(JsonList.Converter<string>(), JsonList.Comparer<string>());
        movie.Property(m => m.Genres).HasConversion(JsonList.Converter<string>(), JsonList.Comparer<string>());
        movie.Property(m => m.Cast).HasConversion(JsonList.Converter<CastMember>(), JsonList.Comparer<CastMember>());
        movie.HasIndex(m => m.TmdbId);
        movie.Property(m => m.PosterPath).HasMaxLength(128);

        movie.Ignore(m => m.Details);
        movie.Ignore(m => m.LastAddedAt);

        movie.OwnsMany(m => m.Formats, format =>
        {
            format.ToTable("MovieFormats");
            format.WithOwner().HasForeignKey("MovieId");
            format.HasKey("MovieId", nameof(MovieFormat.Format));
            format.Property(f => f.Format).HasConversion<string>().HasMaxLength(16);
            format.Property(f => f.Upc).HasConversion(upc => upc!.Value, value => Upc.Parse(value)).HasMaxLength(13);
            format.HasIndex(f => f.Upc);
        });
        movie.Navigation(m => m.Formats).HasField("_formats").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
