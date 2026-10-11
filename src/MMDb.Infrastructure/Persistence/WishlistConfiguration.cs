using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMDb.Application.Movies;
using MMDb.Application.Wishlist;
using MMDb.Domain.Comics;
using MMDb.Domain.Movies;
using MMDb.Domain.Wishlist;

namespace MMDb.Infrastructure.Persistence;

internal sealed class WishlistMovieConfiguration : IEntityTypeConfiguration<WishlistMovie>
{
    public void Configure(EntityTypeBuilder<WishlistMovie> movie)
    {
        movie.ToTable("WishlistMovies");
        movie.HasKey(m => m.Id);
        movie.Property(m => m.Id).ValueGeneratedNever();
        movie.Property(m => m.Version).IsConcurrencyToken();

        movie.Property(m => m.Title).IsRequired().HasMaxLength(MovieDetails.MaxTitleLength);
        movie.Property(m => m.Directors).HasConversion(JsonList.Converter<string>(), JsonList.Comparer<string>());
        movie.Property(m => m.Format).HasConversion<string>().HasMaxLength(16);
        movie.Property(m => m.PosterPath).HasMaxLength(128);
        movie.HasIndex(m => m.TmdbId);
    }
}

internal sealed class WishlistComicConfiguration : IEntityTypeConfiguration<WishlistComic>
{
    public void Configure(EntityTypeBuilder<WishlistComic> comic)
    {
        comic.ToTable("WishlistComics");
        comic.HasKey(c => c.Id);
        comic.Property(c => c.Id).ValueGeneratedNever();
        comic.Property(c => c.Version).IsConcurrencyToken();

        comic.Property(c => c.Format).HasConversion<string>().HasMaxLength(16);
        comic.Property(c => c.Series).HasMaxLength(ComicDetails.MaxTextLength);
        comic.Property(c => c.Number).HasMaxLength(ComicDetails.MaxNumberLength);
        comic.Property(c => c.Title).HasMaxLength(ComicDetails.MaxTextLength);
        comic.Property(c => c.Publisher).HasMaxLength(ComicDetails.MaxShortTextLength);
        comic.Property(c => c.Isbn).HasMaxLength(13);
        comic.Property(c => c.CoverUrl).HasMaxLength(ComicDetails.MaxCoverUrlLength);

        comic.Ignore(c => c.DisplayName);
    }
}

internal sealed class EfWishlistRepository(ShelfDbContext db) : IWishlistRepository
{
    public async Task<IReadOnlyList<WishlistMovie>> ListMoviesAsync(CancellationToken cancellationToken = default) =>
        await db.WishlistMovies.AsNoTracking().ToListAsync(cancellationToken);

    public Task<WishlistMovie?> FindMovieAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WishlistMovies.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<WishlistMovie>> FindMoviesForAsync(IReadOnlyCollection<int> tmdbIds, CancellationToken cancellationToken = default) =>
        await db.WishlistMovies.Where(m => m.TmdbId == null || tmdbIds.Contains(m.TmdbId.Value)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WishlistMovie>> FindMoviesMissingCoverAsync(CancellationToken cancellationToken = default) =>
        await db.WishlistMovies.Where(m => m.CoverImage == null && m.PosterPath != null).ToListAsync(cancellationToken);

    public void Add(WishlistMovie movie) => db.WishlistMovies.Add(movie);

    public void Remove(WishlistMovie movie) => db.WishlistMovies.Remove(movie);

    public async Task<IReadOnlyList<WishlistComic>> ListComicsAsync(CancellationToken cancellationToken = default) =>
        await db.WishlistComics.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WishlistComic>> LoadComicsAsync(CancellationToken cancellationToken = default) =>
        await db.WishlistComics.ToListAsync(cancellationToken);

    public Task<WishlistComic?> FindComicAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.WishlistComics.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(WishlistComic comic) => db.WishlistComics.Add(comic);

    public void Remove(WishlistComic comic) => db.WishlistComics.Remove(comic);

    public async Task<(int Movies, int Comics)> CountAsync(CancellationToken cancellationToken = default) =>
        (await db.WishlistMovies.CountAsync(cancellationToken), await db.WishlistComics.CountAsync(cancellationToken));

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The wishlist item was changed or removed since it was loaded.", ex);
        }
    }
}
