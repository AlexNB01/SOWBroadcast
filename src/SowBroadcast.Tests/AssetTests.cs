using System.Text.Json.Nodes;
using SowBroadcast.Core.Assets;

namespace SowBroadcast.Tests;

public sealed class AssetTests : IDisposable
{
    private static readonly string FixturesAssets = Path.Combine(AppContext.BaseDirectory, "Fixtures", "assets");
    private static readonly string GoldenAssets = Path.Combine(AppContext.BaseDirectory, "Golden", "assets");

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-assets-" + Guid.NewGuid().ToString("N"));
    private string UserRoot => Path.Combine(_tmp, "user", "Scoreboard");
    private string ShippedRoot => Path.Combine(_tmp, "shipped", "Scoreboard");

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private string Fx(string name) => Path.Combine(FixturesAssets, name);

    private AssetService NewService(bool withShipped = false)
    {
        var shipped = withShipped ? new[] { ShippedRoot } : Array.Empty<string>();
        return new AssetService(UserRoot, shipped);
    }

    // ---- slug and file names -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Ana", "ana")]
    [InlineData("Lúcio", "lucio")]
    [InlineData("D.Va", "d-va")]
    [InlineData("King's Row", "king-s-row")]
    [InlineData("Esperança", "esperanca")]
    [InlineData("Soldier: 76", "soldier-76")]
    [InlineData("  Watchpoint: Gibraltar  ", "watchpoint-gibraltar")]
    [InlineData("Torbjörn", "torbjorn")]
    [InlineData("", "item")]
    public void Slug_matches_the_legacy_rules(string name, string expected) => Assert.Equal(expected, Slug.Make(name));

    [Fact]
    public void Names_without_latin_letters_get_distinct_stable_slugs_instead_of_all_colliding()
    {
        var a = Slug.Make("ネコ");
        var b = Slug.Make("イヌ");

        Assert.StartsWith("item-", a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, Slug.Make("ネコ"));
    }

    [Theory]
    [InlineData("D.Va", "Dva")]
    [InlineData("Lúcio", "Lucio")]
    [InlineData("Soldier: 76", "Soldier_76")]
    [InlineData("Torbjörn", "Torbjörn")]
    [InlineData("Wrecking Ball", "Wrecking_Ball")]
    [InlineData("Junker Queen", "Junker_Queen")]
    [InlineData("Jetpack Cat", "Jetpack_Cat")]
    [InlineData("ana", "Ana")]
    [InlineData("mei", "Mei")]
    [InlineData("cat woman", "Cat_Woman")]
    [InlineData("", "Hero")]
    public void Hero_body_file_names_match_the_overlay_lookup(string name, string expected) =>
        Assert.Equal(expected, Slug.HeroBodyFile(name));

    [Theory]
    [InlineData("d-va.png", "D Va")]
    [InlineData("king_s_row.png", "King S Row")]
    [InlineData("busan.webp", "Busan")]
    public void Names_are_guessed_from_file_names(string file, string expected) =>
        Assert.Equal(expected, Slug.NameFromFileName(file));

    // ---- golden: export matches the legacy app -----------------------------------------------------------

    private static List<Asset> LegacyFixtureHeroes(string fx) => new()
    {
        new Asset { Name = "Ana", SourcePath = Path.Combine(fx, "ana.png"), Role = "Support" },
        new Asset { Name = "D.Va", SourcePath = Path.Combine(fx, "dva.png"), Role = "Tank", BodySourcePath = Path.Combine(fx, "dva_body.png") },
        new Asset { Name = "Lúcio", SourcePath = Path.Combine(fx, "lucio.png"), Role = "Support", BodySourcePath = Path.Combine(fx, "lucio_body.png") },
    };

    [Fact]
    public void Exported_files_match_what_the_legacy_app_wrote()
    {
        var exporter = new AssetExporter(UserRoot, Array.Empty<string>());
        exporter.Export(AssetCategory.Heroes, LegacyFixtureHeroes(FixturesAssets));
        exporter.Export(AssetCategory.Maps, new List<Asset>
        {
            new() { Name = "Busan", SourcePath = Fx("busan.png"), Mode = "Control" },
            new() { Name = "King's Row", SourcePath = Fx("kingsrow.png"), Mode = "Hybrid" },
            new() { Name = "Esperança", SourcePath = Fx("esperanca.png"), Mode = "Push" },
        });
        exporter.Export(AssetCategory.Modes, new List<Asset>
        {
            new() { Name = "Control", SourcePath = Fx("control.png") },
            new() { Name = "Hybrid", SourcePath = Fx("hybrid.png") },
        });

        var expectedFiles = Directory.GetFiles(GoldenAssets, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(GoldenAssets, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var actualFiles = Directory.GetFiles(UserRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(UserRoot, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedFiles, actualFiles);

        foreach (var relative in expectedFiles)
        {
            var expected = File.ReadAllBytes(Path.Combine(GoldenAssets, relative));
            var actual = File.ReadAllBytes(Path.Combine(UserRoot, relative));
            if (relative.EndsWith(".json"))
                Assert.Equal(JsonNode.Parse(expected)!.ToJsonString(), JsonNode.Parse(actual)!.ToJsonString());
            else
                Assert.True(expected.SequenceEqual(actual), $"{relative} differs");
        }
    }

    [Fact]
    public void Index_text_is_sorted_by_code_point_with_crlf()
    {
        var exporter = new AssetExporter(UserRoot, Array.Empty<string>());
        exporter.Export(AssetCategory.Maps, new List<Asset> { new() { Name = "b" }, new() { Name = "B" }, new() { Name = "a" } });

        Assert.Equal("B\r\na\r\nb\r\n", File.ReadAllText(Path.Combine(UserRoot, "Maps", "index.txt")));
    }

    [Fact]
    public void Re_exporting_unchanged_assets_does_not_rewrite_files()
    {
        var exporter = new AssetExporter(UserRoot, Array.Empty<string>());
        var heroes = LegacyFixtureHeroes(FixturesAssets);
        exporter.Export(AssetCategory.Heroes, heroes);
        var png = Path.Combine(UserRoot, "Heroes", "ana.png");
        var stamp = File.GetLastWriteTimeUtc(png);
        Thread.Sleep(1100);

        exporter.Export(AssetCategory.Heroes, heroes);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(png));
    }

    // ---- loading (user, shipped, scan) -------------------------------------------------------------------

    private void WriteShippedHeroes()
    {
        Directory.CreateDirectory(Path.Combine(ShippedRoot, "Heroes"));
        Directory.CreateDirectory(Path.Combine(ShippedRoot, "HeroesBody"));
        File.Copy(Fx("ana.png"), Path.Combine(ShippedRoot, "Heroes", "ana.png"));
        File.Copy(Fx("dva.png"), Path.Combine(ShippedRoot, "Heroes", "d-va.png"));
        File.Copy(Fx("dva_body.png"), Path.Combine(ShippedRoot, "HeroesBody", "Dva.png"));
        File.WriteAllText(Path.Combine(ShippedRoot, "Heroes", "index.json"), """
            { "heroes": [
              { "name": "Ana", "slug": "ana", "image": "Heroes/ana.png", "role": "Support" },
              { "name": "D.Va", "slug": "d-va", "image": "Heroes/d-va.png", "role": "Tank", "body_image": "HeroesBody/Dva.png" },
              { "name": "Mei", "slug": "mei", "image": "Heroes/mei.png", "role": "" }
            ] }
            """);
    }

    [Fact]
    public void Loads_the_shipped_catalogue_when_the_user_has_none()
    {
        WriteShippedHeroes();
        var service = NewService(withShipped: true);

        service.Load();

        Assert.Equal(new[] { "Ana", "D.Va", "Mei" }, service.Names(AssetCategory.Heroes));
        Assert.Equal("Support", service.HeroRole("Ana"));
        Assert.Null(service.HeroRole("Mei")); // blank role stays unset
        Assert.True(File.Exists(service.Find(AssetCategory.Heroes, "D.Va")!.BodySourcePath));
    }

    [Fact]
    public void The_users_index_wins_over_the_shipped_one()
    {
        WriteShippedHeroes();
        Directory.CreateDirectory(Path.Combine(UserRoot, "Heroes"));
        File.WriteAllText(Path.Combine(UserRoot, "Heroes", "index.json"),
            """{ "heroes": [ { "name": "Only Mine", "slug": "only-mine", "image": "Heroes/only-mine.png", "role": "Tank" } ] }""");
        var service = NewService(withShipped: true);

        service.Load();

        Assert.Equal(new[] { "Only Mine" }, service.Names(AssetCategory.Heroes));
    }

    [Fact]
    public void Without_any_index_the_folders_are_scanned_and_named_after_the_files()
    {
        Directory.CreateDirectory(Path.Combine(UserRoot, "Maps"));
        File.Copy(Fx("busan.png"), Path.Combine(UserRoot, "Maps", "busan.png"));
        File.Copy(Fx("kingsrow.png"), Path.Combine(UserRoot, "Maps", "king-s-row.png"));
        var service = NewService();

        service.Load();

        Assert.Equal(new[] { "Busan", "King S Row" }, service.Names(AssetCategory.Maps));
    }

    [Fact]
    public void A_corrupt_user_index_falls_back_to_the_shipped_one()
    {
        WriteShippedHeroes();
        Directory.CreateDirectory(Path.Combine(UserRoot, "Heroes"));
        File.WriteAllText(Path.Combine(UserRoot, "Heroes", "index.json"), "{ not json");
        var service = NewService(withShipped: true);

        service.Load();

        Assert.Equal(3, service.Names(AssetCategory.Heroes).Count);
    }

    // ---- add / update / delete ---------------------------------------------------------------------------

    [Fact]
    public void Adding_an_asset_needs_a_name()
    {
        var service = NewService();

        var result = service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "   " });

        Assert.False(result.Ok);
        Assert.Equal("Missing name", result.ErrorTitle);
    }

    [Fact]
    public void A_hero_needs_a_body_image()
    {
        var service = NewService();

        var result = service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Mizuki", Role = "Support", ImageSourcePath = Fx("ana.png") });

        Assert.False(result.Ok);
        Assert.Equal("Missing body image", result.ErrorTitle);
        Assert.Empty(service.Names(AssetCategory.Heroes));
    }

    [Fact]
    public void Adding_a_hero_copies_both_images_and_writes_the_index_straight_away()
    {
        var service = NewService();
        var changed = new List<AssetCategory>();
        service.Changed += changed.Add;

        var result = service.AddOrUpdate(new AssetEdit
        {
            Category = AssetCategory.Heroes, Name = "Lúcio", Role = "Support",
            ImageSourcePath = Fx("lucio.png"), BodySourcePath = Fx("lucio_body.png"),
        });

        Assert.True(result.Ok);
        Assert.True(File.Exists(Path.Combine(UserRoot, "Heroes", "lucio.png")));
        Assert.True(File.Exists(Path.Combine(UserRoot, "HeroesBody", "Lucio.png")));
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(UserRoot, "Heroes", "index.json")))!["heroes"]!.AsArray();
        Assert.Equal("HeroesBody/Lucio.png", index[0]!["body_image"]!.GetValue<string>());
        Assert.Equal("Support", index[0]!["role"]!.GetValue<string>());
        Assert.Equal(new[] { AssetCategory.Heroes }, changed);
    }

    [Fact]
    public void Updating_keeps_the_existing_images_when_none_are_picked()
    {
        var service = NewService();
        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Ana", Role = "Support", ImageSourcePath = Fx("ana.png"), BodySourcePath = Fx("dva_body.png") });

        var result = service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Ana", Role = "Damage" });

        Assert.True(result.Ok);
        Assert.Equal("Damage", service.HeroRole("Ana"));
        Assert.Single(service.Names(AssetCategory.Heroes));
        Assert.NotNull(service.Find(AssetCategory.Heroes, "Ana")!.SourcePath);
    }

    [Fact]
    public void Maps_keep_their_mode_and_other_categories_ignore_role()
    {
        var service = NewService();

        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", Role = "Tank", ImageSourcePath = Fx("busan.png") });

        var map = service.Find(AssetCategory.Maps, "Busan")!;
        Assert.Equal("Control", map.Mode);
        Assert.Null(map.Role);
    }

    [Fact]
    public void Deleting_removes_the_entry_the_exported_image_and_rewrites_the_index()
    {
        var service = NewService();
        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", ImageSourcePath = Fx("busan.png") });
        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Ilios", Mode = "Control", ImageSourcePath = Fx("kingsrow.png") });

        Assert.True(service.Delete(AssetCategory.Maps, "Busan"));

        Assert.Equal(new[] { "Ilios" }, service.Names(AssetCategory.Maps));
        Assert.False(File.Exists(Path.Combine(UserRoot, "Maps", "busan.png")));
        Assert.DoesNotContain("Busan", File.ReadAllText(Path.Combine(UserRoot, "Maps", "index.json")));
        Assert.False(service.Delete(AssetCategory.Maps, "Busan"));
    }

    [Fact]
    public void Deleting_a_shipped_asset_never_touches_the_shipped_files()
    {
        WriteShippedHeroes();
        var service = NewService(withShipped: true);
        service.Load();

        service.Delete(AssetCategory.Heroes, "Ana");

        Assert.True(File.Exists(Path.Combine(ShippedRoot, "Heroes", "ana.png")));
        var reloaded = NewService(withShipped: true);
        reloaded.Load();
        Assert.DoesNotContain("Ana", reloaded.Names(AssetCategory.Heroes)); // the user's index now wins
    }

    [Fact]
    public void Shipped_images_are_not_copied_into_the_users_folder()
    {
        WriteShippedHeroes();
        var service = NewService(withShipped: true);
        service.Load();

        var result = service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "D.Va", Role = "Damage" });

        Assert.True(result.Ok);
        Assert.False(File.Exists(Path.Combine(UserRoot, "Heroes", "d-va.png")));
        Assert.False(File.Exists(Path.Combine(UserRoot, "HeroesBody", "Dva.png")));
        Assert.Contains("\"role\": \"Damage\"", File.ReadAllText(Path.Combine(UserRoot, "Heroes", "index.json")));
    }

    // ---- bulk import -------------------------------------------------------------------------------------

    [Fact]
    public void Scan_finds_new_pictures_but_not_the_ones_already_catalogued()
    {
        Directory.CreateDirectory(Path.Combine(UserRoot, "Heroes"));
        File.Copy(Fx("ana.png"), Path.Combine(UserRoot, "Heroes", "ana.png"));
        File.Copy(Fx("dva.png"), Path.Combine(UserRoot, "Heroes", "d-va.png"));
        File.Copy(Fx("lucio.png"), Path.Combine(UserRoot, "Heroes", "new-hero.png"));
        var service = NewService();
        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Ana", Role = "Support", ImageSourcePath = Fx("ana.png"), BodySourcePath = Fx("dva_body.png") });
        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "D.Va", Role = "Tank", ImageSourcePath = Fx("dva.png"), BodySourcePath = Fx("dva_body.png") });

        var found = service.ScanForImport();

        Assert.Equal(new[] { "New Hero" }, found.Select(c => c.Name));
    }

    [Fact]
    public void Bulk_import_adds_new_names_skips_existing_ones_and_reports_counts()
    {
        var service = NewService();
        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", ImageSourcePath = Fx("busan.png") });

        var (heroes, maps) = service.BulkImport(new[]
        {
            new ImportCandidate(AssetCategory.Heroes, Fx("lucio.png"), "New Hero"),
            new ImportCandidate(AssetCategory.Maps, Fx("busan.png"), "Busan"),
            new ImportCandidate(AssetCategory.Maps, Fx("kingsrow.png"), "Havana", "Escort"),
            new ImportCandidate(AssetCategory.Maps, Fx("esperanca.png"), "  "),
        });

        Assert.Equal((1, 1), (heroes, maps));
        Assert.Equal("Escort", service.Find(AssetCategory.Maps, "Havana")!.Mode);
        Assert.True(File.Exists(Path.Combine(UserRoot, "Maps", "havana.png")));
        Assert.True(File.Exists(Path.Combine(UserRoot, "Heroes", "new-hero.png")));
    }
}
