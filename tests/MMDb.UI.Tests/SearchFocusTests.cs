using Bunit;
using Microsoft.AspNetCore.Components;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>The add screens open ready to type: the cursor is in the search box.</summary>
public sealed class SearchFocusTests : ShelfTestContext
{
    private IRenderedComponent<TPage> Open<TPage>(string location)
        where TPage : IComponent
    {
        Navigation.NavigateTo(location);
        var page = Render<TPage>();
        page.WaitForElement(".lookup-bar input");
        return page;
    }

    private void TheCursorIsIn<TPage>(IRenderedComponent<TPage> page)
        where TPage : IComponent =>
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(page.Find(".lookup-bar input"));

    [Fact]
    public void Add_movie_opens_with_the_cursor_in_the_search_box()
    {
        TheCursorIsIn(Open<MovieEditor>("movies/new"));
    }

    [Fact]
    public void Add_comic_opens_with_the_cursor_in_the_search_box()
    {
        TheCursorIsIn(Open<ComicEditor>("comics/new"));
    }

    [Fact]
    public void Adding_a_film_to_the_wishlist_opens_with_the_cursor_in_the_search_box()
    {
        TheCursorIsIn(Open<WishlistAdd>("wishlist/movies/new"));
    }

    [Fact]
    public void Adding_a_comic_to_the_wishlist_opens_with_the_cursor_in_the_search_box()
    {
        TheCursorIsIn(Open<WishlistAddComic>("wishlist/comics/new"));
    }

    [Fact]
    public void On_the_pc_a_search_made_on_opening_keeps_the_cursor_in_the_box()
    {
        var page = Open<MovieEditor>("movies/new?q=heat");

        page.Find(".lookup-bar input").GetAttribute("value").ShouldBe("heat");
        TheCursorIsIn(page);
    }
}

/// <summary>
/// On the phone the cursor brings the keyboard up, so only an empty search box gets it: a search already made is
/// left in view.
/// </summary>
public sealed class PhoneSearchFocusTests : PhoneTestContext
{
    private const string Focus = "Blazor._internal.domWrapper.focus";

    private IRenderedComponent<TPage> Open<TPage>(string location)
        where TPage : IComponent
    {
        Navigation.NavigateTo(location);
        var page = Render<TPage>();
        page.WaitForElement(".lookup-bar input");
        return page;
    }

    [Fact]
    public void Add_movie_opens_with_the_cursor_in_the_empty_box()
    {
        var page = Open<MovieEditor>("movies/new");

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(page.Find(".lookup-bar input"));
    }

    [Fact]
    public void Add_comic_opens_with_the_cursor_in_the_empty_box()
    {
        var page = Open<ComicEditor>("comics/new");

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(page.Find(".lookup-bar input"));
    }

    [Fact]
    public void A_film_searched_for_on_opening_is_not_covered_by_the_keyboard()
    {
        var page = Open<MovieEditor>("movies/new?q=heat");

        page.Find(".lookup-bar input").GetAttribute("value").ShouldBe("heat");
        JSInterop.VerifyNotInvoke(Focus);
    }

    [Fact]
    public void A_comic_searched_for_on_opening_is_not_covered_by_the_keyboard()
    {
        var page = Open<ComicEditor>("comics/new?q=saga%207");

        page.Find(".lookup-bar input").GetAttribute("value").ShouldBe("saga 7");
        JSInterop.VerifyNotInvoke(Focus);
    }
}
