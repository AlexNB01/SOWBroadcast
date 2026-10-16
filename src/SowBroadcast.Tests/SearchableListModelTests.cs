using SowBroadcast.App.Controls;

namespace SowBroadcast.Tests;

public sealed class SearchableListModelTests
{
    private static SearchableListModel Heroes(string? none = null)
    {
        var model = new SearchableListModel { NoneText = none };
        model.SetItems(new[] { "Ana", "Ashe", "Baptiste", "D.Va", "Junker Queen", "Mei" });
        return model;
    }

    [Fact]
    public void Filter_is_a_case_insensitive_substring_match_that_keeps_the_original_order()
    {
        var model = Heroes();

        model.Query = "A";

        Assert.Equal(new[] { "Ana", "Ashe", "Baptiste", "D.Va" }, model.Visible);
    }

    [Fact]
    public void Filter_matches_inside_words_not_only_at_the_start()
    {
        var model = Heroes();

        model.Query = "queen";

        Assert.Equal(new[] { "Junker Queen" }, model.Visible);
    }

    [Fact]
    public void An_empty_query_shows_everything()
    {
        var model = Heroes();
        model.Query = "zzz";

        model.Query = "";

        Assert.Equal(6, model.Visible.Count);
    }

    [Fact]
    public void Typing_moves_the_current_row_to_the_first_match_and_to_nothing_when_nothing_matches()
    {
        var model = Heroes();

        model.Query = "ba";
        Assert.Equal("Baptiste", model.CurrentRow);

        model.Query = "xyz";
        Assert.Null(model.CurrentRow);
        Assert.Equal(-1, model.CurrentIndex);
    }

    [Fact]
    public void Arrow_keys_move_through_the_visible_rows_and_stop_at_the_ends()
    {
        var model = Heroes();
        model.Query = "a";

        model.Move(-1);
        Assert.Equal("Ana", model.CurrentRow);

        model.Move(+1);
        model.Move(+1);
        Assert.Equal("Baptiste", model.CurrentRow);

        model.Move(+10);
        Assert.Equal("D.Va", model.CurrentRow);
    }

    [Fact]
    public void Opening_puts_the_current_row_on_the_selected_item_and_clears_the_filter()
    {
        var model = Heroes();
        model.Query = "zzz";

        model.Open("Junker Queen");

        Assert.Equal(6, model.Visible.Count);
        Assert.Equal("Junker Queen", model.CurrentRow);
    }

    [Fact]
    public void Opening_with_a_value_that_is_not_in_the_list_starts_on_the_first_row()
    {
        var model = Heroes();

        model.Open("Not a hero");

        Assert.Equal("Ana", model.CurrentRow);
    }

    [Fact]
    public void A_none_entry_comes_first_and_stands_for_no_value()
    {
        var model = Heroes(none: "— Hero —");

        model.Open(null);

        Assert.Equal("— Hero —", model.Visible[0]);
        Assert.Equal("— Hero —", model.CurrentRow);
        Assert.Null(model.ValueOf(model.CurrentRow));
        Assert.Equal("Ana", model.ValueOf("Ana"));
    }

    [Fact]
    public void Setting_new_items_while_a_filter_is_active_keeps_the_current_row_in_range()
    {
        var model = Heroes();
        model.Query = "a";
        model.Move(+3);

        model.SetItems(new[] { "Ana" });

        Assert.True(model.CurrentIndex < model.Visible.Count);
    }

    [Theory]
    [InlineData(100, 220)]
    [InlineData(260, 260)]
    public void Popup_is_at_least_220_wide(double control, double expected) =>
        Assert.Equal(expected, SearchableListModel.PopupWidth(control));

    [Theory]
    [InlineData(0, 120)]
    [InlineData(1, 120)]
    [InlineData(5, 150)]
    [InlineData(12, 304)]
    [InlineData(54, 320)]
    public void Popup_height_is_forty_plus_22_per_row_clamped(int rows, double expected) =>
        Assert.Equal(expected, SearchableListModel.PopupHeight(rows));
}
