using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FirstMud.Application.Content;
using FirstMud.Domain.Configuration;
using FirstMud.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FirstMud.Tests.Domain;

/// <summary>
/// Tests for the companion XP rubber-band multiplier (task #74). Verifies the
/// accessor formula at the documented examples, the zero/negative-gap floor,
/// the upper clamp, and that the ContentProvider loader falls back to the
/// historical default constants when the JSON block is absent.
/// </summary>
public class ProgressionCurvesAccessorRubberBandTests : IDisposable
{
    public ProgressionCurvesAccessorRubberBandTests()
    {
        ProgressionCurvesAccessor.ResetRubberBandForTests();
    }

    public void Dispose()
    {
        ProgressionCurvesAccessor.ResetRubberBandForTests();
    }

    [Fact]
    public void Defaults_match_spec()
    {
        ProgressionCurvesAccessor.RubberBandPlayerLevelWeight.Should().Be(3);
        ProgressionCurvesAccessor.RubberBandPerGapBonus.Should().BeApproximately(0.15, 1e-9);
        ProgressionCurvesAccessor.RubberBandMaxMultiplier.Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void L12_player_layer1_companion_yields_2_35()
    {
        // 1 + (12 - 1*3) * 0.15 = 1 + 9*0.15 = 2.35
        ProgressionCurvesAccessor.RubberBandMultiplier(12, 1)
            .Should().BeApproximately(2.35, 1e-9);
    }

    [Fact]
    public void L10_player_layer3_companion_near_match_is_near_1()
    {
        // 1 + (10 - 9) * 0.15 = 1.15
        var mult = ProgressionCurvesAccessor.RubberBandMultiplier(10, 3);
        mult.Should().BeApproximately(1.15, 1e-9);
        (mult - 1.0).Should().BeLessThanOrEqualTo(0.15);
    }

    [Fact]
    public void L30_player_layer1_companion_hits_max_clamp()
    {
        // Unclamped: 1 + (30 - 3) * 0.15 = 5.05 → clamped to 3.0
        ProgressionCurvesAccessor.RubberBandMultiplier(30, 1)
            .Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void Companion_ahead_of_player_returns_1_0_no_negative()
    {
        // L5 player, layer-5 companion → gap = 5 - 15 = -10 → clamped floor
        ProgressionCurvesAccessor.RubberBandMultiplier(5, 5)
            .Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Exact_bracket_match_returns_1_0()
    {
        // L3 player, layer-1 companion → gap = 0
        ProgressionCurvesAccessor.RubberBandMultiplier(3, 1)
            .Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void ContentProvider_loads_authored_rubber_band_block_from_real_content_root()
    {
        // Load the real repo content root. The authored file contains the
        // canonical rubberBand block (3, 0.15, 3.0); this test proves the
        // loader wires the JSON through ProgressionCurvesDefinition into the
        // domain-side accessor unchanged.
        var contentRoot = ContentRootResolver.Resolve();
        var provider = new ContentProvider(contentRoot);

        var rb = provider.ProgressionCurves.CompanionRubberBand;
        rb.PlayerLevelWeight.Should().Be(3);
        rb.PerGapBonus.Should().BeApproximately(0.15, 1e-9);
        rb.MaxMultiplier.Should().BeApproximately(3.0, 1e-9);

        ProgressionCurvesAccessor.RubberBandPlayerLevelWeight.Should().Be(3);
    }

    [Fact]
    public void ContentProvider_missing_rubber_band_block_falls_back_to_defaults()
    {
        // Mirror the real content root to a temp dir so every other required
        // file is present, then strip the rubberBand block from the
        // progression-curves.json copy to exercise the fallback branch.
        var realRoot = ContentRootResolver.Resolve();
        using var tmp = new MirroredContentRoot(realRoot);

        var curvesPath = Path.Combine(tmp.Path, "progression-curves.json");
        var body = File.ReadAllText(curvesPath);
        // Strip the whole companionRubberBand key + object.
        var stripped = Regex.Replace(
            body,
            ",\\s*\"companionRubberBand\"\\s*:\\s*\\{[^}]*\\}",
            string.Empty,
            RegexOptions.Singleline);
        File.WriteAllText(curvesPath, stripped);

        var provider = new ContentProvider(tmp.Path);
        var rb = provider.ProgressionCurves.CompanionRubberBand;
        rb.PlayerLevelWeight.Should().Be(ProgressionCurvesAccessor.DefaultRubberBandPlayerLevelWeight);
        rb.PerGapBonus.Should().BeApproximately(ProgressionCurvesAccessor.DefaultRubberBandPerGapBonus, 1e-9);
        rb.MaxMultiplier.Should().BeApproximately(ProgressionCurvesAccessor.DefaultRubberBandMaxMultiplier, 1e-9);
    }

    /// <summary>
    /// Copies the repo content root into a scratch directory so individual
    /// files can be edited without mutating the checked-in content.
    /// </summary>
    private sealed class MirroredContentRoot : IDisposable
    {
        public string Path { get; }

        public MirroredContentRoot(string source)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "fm-rb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            CopyAll(source, Path);
        }

        private static void CopyAll(string src, string dst)
        {
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(src, dst));
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(src, dst), overwrite: true);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
