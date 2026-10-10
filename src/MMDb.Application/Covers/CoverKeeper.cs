using MMDb.Application.Metadata;
using MMDb.Domain;

namespace MMDb.Application.Covers;

/// <summary>Keeps a product's stored cover in step with its cover image online, around the save that changes it.</summary>
internal sealed class CoverKeeper(ICoverStore store)
{
    /// <summary>One file per product and image, so a new cover never overwrites the one in use.</summary>
    public static string CoverName(Guid productId, string fileName) => $"{productId:N}-{fileName}";

    /// <summary>
    /// Downloads the cover if the product needs a new one (<paramref name="wanted"/> isn't the one it has), runs
    /// <paramref name="save"/>, then removes the cover it replaced. If the download fails (offline) the old cover
    /// stays and the next save tries again. If the save fails, the new file is removed and the error passes through.
    /// </summary>
    /// <param name="wanted">The cover's file name in the store; <see langword="null"/> if it has no cover image.</param>
    public async Task SaveAsync(
        ProductBase product, string? wanted, Func<CancellationToken, Task<byte[]?>> fetch, Func<Task> save, CancellationToken cancellationToken)
    {
        var old = product.CoverImage;
        string? written = null;

        if (wanted is null)
        {
            product.UseCover(null);
        }
        else if (wanted != old && await fetch(cancellationToken) is { } image)
        {
            await store.SaveAsync(wanted, image, cancellationToken);
            written = wanted;
            product.UseCover(wanted);
        }

        try
        {
            await save();
        }
        catch
        {
            product.UseCover(old);
            if (written is not null)
            {
                store.Delete(written);
            }

            throw;
        }

        if (old is not null && old != product.CoverImage)
        {
            store.Delete(old);
        }
    }

    public void Forget(ProductBase product)
    {
        if (product.CoverImage is { } cover)
        {
            store.Delete(cover);
        }
    }
}
