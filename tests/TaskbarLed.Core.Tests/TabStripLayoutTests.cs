using TaskbarLed.Core.Presentation;

namespace TaskbarLed.Core.Tests;

public class TabStripLayoutTests
{
    [Fact]
    public void Tabs_that_fit_keep_their_full_titles()
    {
        Assert.Equal([100d, 60d], TabStripLayout.Fit([100, 60], 400, 40));
    }

    [Fact]
    public void Longest_titles_are_shortened_first_and_short_ones_stay_whole()
    {
        var widths = TabStripLayout.Fit([200, 50, 180, 60], 330, 40);

        Assert.Equal([110d, 50d, 110d, 60d], widths);
        Assert.Equal(330, widths.Sum(), 3);
    }

    [Fact]
    public void Many_tabs_never_go_below_the_minimum()
    {
        var widths = TabStripLayout.Fit([150, 150, 150, 150, 30], 100, 40);

        Assert.All(widths.Take(4), w => Assert.Equal(40, w));
        Assert.Equal(30, widths[4]);
    }

    [Fact]
    public void One_long_tab_takes_the_whole_row()
    {
        Assert.Equal([300d], TabStripLayout.Fit([500], 300, 40));
    }
}
