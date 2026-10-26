using System.Globalization;
using System.Text;
using Guild.Application;

namespace Guild.Domain.Tests;

public sealed class DisplayTextTests
{
    [Fact]
    public void CatalogIsEmbeddedAndAvailableWithoutSourceFiles()
    {
        var resourceNames = typeof(DisplayText).Assembly.GetManifestResourceNames();
        Assert.Contains("Guild.Application.DisplayText.ja.application.json", resourceNames);
        Assert.NotEmpty(DisplayText.Entries);
        Assert.Equal("筋力 STR", DisplayText.Get("app.parameter.str"));
        Assert.Equal("Rank 2 → 3", DisplayText.Format("app.rank.transition", 2, 3));
    }

    [Fact]
    public void MissingAndCaseMismatchedKeysAreNotSilentlyShownToPlayers()
    {
        Assert.Throws<KeyNotFoundException>(() => DisplayText.Get("missing.key"));
        Assert.Throws<KeyNotFoundException>(() => DisplayText.Get("App.parameter.str"));
        Assert.Throws<ArgumentException>(() => DisplayText.Get(" "));
    }

    [Fact]
    public void FormattingIsIndependentOfOperatingSystemCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
            Assert.Equal("HP 12/20", DisplayText.Format("app.hp.current", 12, 20));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void EveryEmbeddedTemplateHasValidCompositePlaceholders()
    {
        foreach (var (key, value) in DisplayText.Entries)
        {
            var exception = Record.Exception(() => CompositeFormat.Parse(value));
            Assert.True(exception is null, $"Invalid display text template {key}: {exception?.Message}");
        }
    }
}
