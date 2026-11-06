using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PrintShelf.Web.Components.Shared;
using PrintShelf.Web.Data.Entities;
using PrintShelf.Web.Services;

namespace PrintShelf.Web.Tests;

/// <summary>Thin bUnit layer (docs/05-testing.md §3): filter toolbar + card rendering.</summary>
public class CatalogToolbarTests : BunitContext
{
    private static readonly List<TagUsage> Tags =
    [
        new(Guid.NewGuid(), "functional", "Functional", 3),
        new(Guid.NewGuid(), "decor", "Decor", 7)
    ];

    [Fact]
    public void Renders_search_categories_and_tag_options()
    {
        var cut = Render<CatalogToolbar>(p => p
            .Add(x => x.Categories, new[] { "Decor", "Tools" })
            .Add(x => x.Tags, Tags));

        Assert.NotNull(cut.Find("input[name=search]"));
        var options = cut.FindAll("select[name=tag] option").Select(o => o.TextContent).ToList();
        Assert.Contains(options, o => o.Contains("Functional"));
        Assert.Contains(options, o => o.Contains("Decor (7)"));
        Assert.Contains(cut.FindAll("select[name=category] option"), o => o.TextContent == "Tools");
    }

    [Fact]
    public void Submitting_search_emits_filters()
    {
        CatalogFilters? emitted = null;
        var cut = Render<CatalogToolbar>(p => p
            .Add(x => x.Tags, Tags)
            .Add(x => x.Changed, EventCallback.Factory.Create<CatalogFilters>(this, f => emitted = f)));

        cut.Find("input[name=search]").Input("gear");
        cut.Find("form").Submit();

        Assert.NotNull(emitted);
        Assert.Equal("gear", emitted!.Search);
        Assert.Equal("newest", emitted.Sort);
        Assert.Equal("grid", emitted.View);
    }

    [Fact]
    public void View_toggle_emits_list_view()
    {
        CatalogFilters? emitted = null;
        var cut = Render<CatalogToolbar>(p => p
            .Add(x => x.Changed, EventCallback.Factory.Create<CatalogFilters>(this, f => emitted = f)));

        var list = cut.FindAll("button").First(b => b.TextContent.Contains("List"));
        list.Click();

        Assert.Equal("list", emitted?.View);
    }

    [Fact]
    public void Tag_filter_emits_selected_tag_guid()
    {
        CatalogFilters? emitted = null;
        var tag = Tags[1];
        var cut = Render<CatalogToolbar>(p => p
            .Add(x => x.Tags, Tags)
            .Add(x => x.Changed, EventCallback.Factory.Create<CatalogFilters>(this, f => emitted = f)));

        cut.Find("select[name=tag]").Change(tag.Id.ToString());

        Assert.Equal(tag.Id, emitted?.TagId);
    }

    [Fact]
    public void Clear_button_appears_only_when_a_filter_is_active()
    {
        var cut = Render<CatalogToolbar>(p => p.Add(x => x.Search, "cube"));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Clear filters"));

        var clean = Render<CatalogToolbar>();
        Assert.DoesNotContain(clean.FindAll("button"), b => b.TextContent.Contains("Clear filters"));
    }
}

public class ModelCardTests : BunitContext
{
    private static ModelSummary Model(bool hasThumb) => new(
        Guid.NewGuid(), "Benchy", "Boats", new string('a', 64), 1_048_576, 225_000,
        60.0m, 31.0m, 48.0m, 15_000.5m, "mm", DateTimeOffset.UtcNow.AddDays(-2),
        ["fun", "boat"], 4, hasThumb);

    [Fact]
    public void Renders_name_dimensions_tags_and_print_count()
    {
        var cut = Render<ModelCard>(p => p.Add(x => x.Model, Model(false)));

        Assert.Contains("Benchy", cut.Markup);
        Assert.Contains("60 × 31 × 48 mm", cut.Markup);
        Assert.Contains("fun", cut.Markup);
        Assert.Contains("4 prints", cut.Markup);
    }

    [Fact]
    public void Uses_thumbnail_url_only_when_a_thumb_exists()
    {
        var withThumb = Render<ModelCard>(p => p.Add(x => x.Model, Model(true)));
        var img = withThumb.Find("img");
        Assert.Contains("/thumbs/", img.GetAttribute("src"));

        var noThumb = Render<ModelCard>(p => p.Add(x => x.Model, Model(false)));
        Assert.Empty(noThumb.FindAll("img"));
        Assert.Contains("Preview to create thumbnail", noThumb.Markup);
    }
}

public class ConfirmButtonTests : BunitContext
{
    [Fact]
    public void Requires_two_clicks_before_confirming()
    {
        int confirmations = 0;
        var cut = Render<ConfirmButton>(p => p
            .Add(x => x.Text, "Delete")
            .Add(x => x.ConfirmText, "Sure?")
            .Add(x => x.OnConfirm, () => confirmations++));

        var button = cut.Find("button");
        button.Click();
        Assert.Equal(0, confirmations);
        Assert.Contains("Sure?", button.TextContent);

        cut.Find("button").Click();
        Assert.Equal(1, confirmations);
    }
}

public class UploadValidationTests
{
    private const long Max = 200L * 1024 * 1024;

    [Theory]
    [InlineData("cube.stl", 100, null)]
    [InlineData("CUBE.STL", 100, null)]
    public void Accepts_stl_extensions(string name, long bytes, string? expected)
    {
        Assert.Equal(expected, UploadValidation.Validate(name, bytes, Max));
    }

    [Theory]
    [InlineData("cube.obj")]
    [InlineData("model.stl.exe")]
    [InlineData("noextension")]
    public void Rejects_non_stl(string name)
    {
        var error = UploadValidation.Validate(name, 100, Max);
        Assert.NotNull(error);
        Assert.Contains(".stl", error);
    }

    [Fact]
    public void Rejects_empty_and_oversize()
    {
        Assert.NotNull(UploadValidation.Validate("cube.stl", 0, Max));
        Assert.NotNull(UploadValidation.Validate("cube.stl", Max + 1, Max));
    }
}

public class ServiceLogicTests
{
    [Fact]
    public void Tag_slugify_normalizes_case_spaces_and_punctuation()
    {
        Assert.Equal("spare-part", TagService.Slugify("  Spare  Part! "));
        Assert.Equal("vase", TagService.Slugify("Vase"));
        Assert.Equal("tag", TagService.Slugify("   "));
        Assert.Equal("a-b-c", TagService.Slugify("a/b_c"));
    }

    [Fact]
    public void Print_aggregate_matches_hand_computed_fixture()
    {
        var runs = new List<PrintRun>
        {
            new() { Printer = "MK4", Result = PrintResults.Success, DurationMin = 60, Cost = 2.50m, PrintedAt = DateTimeOffset.UtcNow.AddDays(-3) },
            new() { Printer = "MK4", Result = PrintResults.Failed, DurationMin = 30, Cost = 1.00m, PrintedAt = DateTimeOffset.UtcNow.AddDays(-2) },
            new() { Printer = "X1C", Result = PrintResults.Partial, DurationMin = 90, Cost = 2.00m, PrintedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Printer = "X1C", Result = PrintResults.Success, DurationMin = 120, Cost = 4.50m, PrintedAt = DateTimeOffset.UtcNow }
        };

        var aggregate = PrintRunService.Aggregate(runs);

        Assert.Equal(4, aggregate.Runs);
        Assert.Equal(2, aggregate.Successes);
        Assert.Equal(1, aggregate.Failures);
        Assert.Equal(1, aggregate.Partial);
        Assert.Equal(300, aggregate.TotalMinutes);
        Assert.Equal(10.00m, aggregate.TotalCost);
        Assert.Equal(50.0, aggregate.SuccessRate, 3);
    }

    [Fact]
    public void Print_aggregate_is_zero_for_no_runs()
    {
        var aggregate = PrintRunService.Aggregate([]);
        Assert.Equal(0, aggregate.Runs);
        Assert.Equal(0, aggregate.SuccessRate);
        Assert.Null(aggregate.LastPrinted);
    }
}
