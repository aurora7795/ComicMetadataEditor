using System.Linq;
using InkTag.Core;
using InkTag.Gui.ViewModels;
using Xunit;

namespace InkTag.Tests;

public class BulkEditEngineTests
{
    [Fact]
    public void BulkEditCatalog_ContainsAllExpectedFields()
    {
        Assert.NotEmpty(BulkEditCatalog.AllFields);
        Assert.Contains(BulkEditCatalog.AllFields, f => f.PropertyName == "Series");
        Assert.Contains(BulkEditCatalog.AllFields, f => f.PropertyName == "Tags");
        Assert.Contains(BulkEditCatalog.AllFields, f => f.PropertyName == "Writer");
        Assert.Contains(BulkEditCatalog.AllFields, f => f.PropertyName == "MangaDirection");
        Assert.Contains(BulkEditCatalog.AllFields, f => f.PropertyName == "AgeRating");
    }

    [Fact]
    public void ApplyBulkRule_SetAndAppendStringValues()
    {
        var item = new ComicItemViewModel("test.cbz", new ComicInfo { Title = "Issue #1", Tags = "Marvel" });

        var setRule = new BulkEditRuleViewModel(BulkEditCatalog.AllFields.First(f => f.PropertyName == "Writer"))
        {
            SelectedOperation = BulkEditOperation.Set,
            StringValue = "Stan Lee"
        };

        var appendRule = new BulkEditRuleViewModel(BulkEditCatalog.AllFields.First(f => f.PropertyName == "Tags"))
        {
            SelectedOperation = BulkEditOperation.Append,
            StringValue = "Action"
        };

        item.ApplyBulkRule(setRule);
        item.ApplyBulkRule(appendRule);

        Assert.Equal("Stan Lee", item.Writer);
        Assert.Equal("Marvel, Action", item.Tags);
        Assert.True(item.IsDirty);
    }

    [Fact]
    public void ApplyBulkRule_NumericAndEnumRules()
    {
        var item = new ComicItemViewModel("test.cbz", new ComicInfo());

        var yearRule = new BulkEditRuleViewModel(BulkEditCatalog.AllFields.First(f => f.PropertyName == "Year"))
        {
            SelectedOperation = BulkEditOperation.Set,
            NumericValue = 2024
        };

        var mangaRule = new BulkEditRuleViewModel(BulkEditCatalog.AllFields.First(f => f.PropertyName == "MangaDirection"))
        {
            SelectedOperation = BulkEditOperation.Set,
            SelectedEnumOption = "YesAndRightToLeft"
        };

        item.ApplyBulkRule(yearRule);
        item.ApplyBulkRule(mangaRule);

        Assert.Equal(2024, item.Year);
        Assert.Equal(MangaDirection.YesAndRightToLeft, item.MangaDirection);
        Assert.True(item.Manga);

        var model = new ComicInfo();
        item.ApplyChangesToModel(model);
        Assert.Equal(2024, model.Year);
        Assert.Equal(MangaDirection.YesAndRightToLeft, model.Manga);
    }

    [Fact]
    public void ApplyBulkRule_ReplaceOperation()
    {
        var item = new ComicItemViewModel("test.cbz", new ComicInfo { Series = "The Amazing Spider-Man" });

        var replaceRule = new BulkEditRuleViewModel(BulkEditCatalog.AllFields.First(f => f.PropertyName == "Series"))
        {
            SelectedOperation = BulkEditOperation.Replace,
            FindValue = "Spider-Man",
            ReplaceValue = "Spider-Man (Vol 1)"
        };

        item.ApplyBulkRule(replaceRule);

        Assert.Equal("The Amazing Spider-Man (Vol 1)", item.Series);
    }

    [Fact]
    public void ApplyBulkRule_ClearAndPrependOperations()
    {
        var item = new ComicItemViewModel("test.cbz", new ComicInfo
        {
            Notes = "Existing note",
            Summary = "Story details"
        });

        var prependRule = new BulkEditRuleViewModel(BulkEditCatalog.AllFields.First(f => f.PropertyName == "Notes"))
        {
            SelectedOperation = BulkEditOperation.Prepend,
            StringValue = "Important:"
        };

        var clearRule = new BulkEditRuleViewModel(BulkEditCatalog.AllFields.First(f => f.PropertyName == "Summary"))
        {
            SelectedOperation = BulkEditOperation.Clear
        };

        item.ApplyBulkRule(prependRule);
        item.ApplyBulkRule(clearRule);

        Assert.Equal("Important: Existing note", item.Notes);
        Assert.Null(item.Summary);
        Assert.True(item.IsDirty);
    }
}
