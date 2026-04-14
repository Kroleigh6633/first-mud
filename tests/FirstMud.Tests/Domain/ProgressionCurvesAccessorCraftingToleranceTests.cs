using System.IO;
using System.Text.RegularExpressions;
using FirstMud.Application.Content;
using FirstMud.Domain.Configuration;
using FluentAssertions;
using Xunit;

namespace FirstMud.Tests.Domain;

/// <summary>
/// Tests for the skill-scaled crafting tolerance accessor (task #133).
///
/// Formula:
///   skillRatio = playerSkill / max(1, requiredSkill)
///   tolerance  = clamp(base + max(0, skillRatio - 1) * perSkillTier, base, max)
///
/// Defaults (from content/progression-curves.json):
///   base = 0.05, perSkillTier = 0.02, max = 0.20.
/// </summary>
public class ProgressionCurvesAccessorCraftingToleranceTests : IDisposable
{
    public ProgressionCurvesAccessorCraftingToleranceTests()
    {
        ProgressionCurvesAccessor.ResetCraftingSkillScalingForTests();
    }

    public void Dispose()
    {
        ProgressionCurvesAccessor.ResetCraftingSkillScalingForTests();
    }

    [Fact]
    public void Defaults_match_spec()
    {
        ProgressionCurvesAccessor.CraftingBaseTolerance.Should().BeApproximately(0.05, 1e-9);
        ProgressionCurvesAccessor.CraftingTolerancePerSkillTier.Should().BeApproximately(0.02, 1e-9);
        ProgressionCurvesAccessor.CraftingMaxTolerance.Should().BeApproximately(0.20, 1e-9);
    }

    [Fact]
    public void Skill_equal_to_requirement_returns_base_tolerance()
    {
        ProgressionCurvesAccessor.CraftingTolerance(2, 2)
            .Should().BeApproximately(0.05, 1e-9);
    }

    [Fact]
    public void Skill_twice_required_adds_one_tier_bonus()
    {
        // skillRatio = 10/5 = 2 → bonus = 1 * 0.02 = 0.02 → total 0.07
        ProgressionCurvesAccessor.CraftingTolerance(10, 5)
            .Should().BeApproximately(0.07, 1e-9);
    }

    [Fact]
    public void Skill_four_times_required_adds_three_tier_bonus()
    {
        // skillRatio = 20/5 = 4 → bonus = 3 * 0.02 = 0.06 → total 0.11
        ProgressionCurvesAccessor.CraftingTolerance(20, 5)
            .Should().BeApproximately(0.11, 1e-9);
    }

    [Fact]
    public void Tolerance_is_capped_at_max()
    {
        // skillRatio = 100/5 = 20 → bonus = 19 * 0.02 = 0.38 → clamped to 0.20
        ProgressionCurvesAccessor.CraftingTolerance(100, 5)
            .Should().BeLessThanOrEqualTo(0.20 + 1e-9)
            .And.BeApproximately(0.20, 1e-9);
    }

    [Fact]
    public void Skill_below_requirement_returns_base_tolerance_never_negative()
    {
        // Even if the pre-gate allowed skill < requirement, the floor is base.
        ProgressionCurvesAccessor.CraftingTolerance(1, 10)
            .Should().BeApproximately(0.05, 1e-9);
    }

    [Fact]
    public void Required_skill_zero_is_treated_as_one()
    {
        // Avoids div-by-zero — requiredSkill clamped to 1.
        ProgressionCurvesAccessor.CraftingTolerance(5, 0)
            .Should().BeApproximately(0.05 + 4 * 0.02, 1e-9);
    }

    [Fact]
    public void ContentProvider_loads_authored_crafting_skill_scaling_from_real_content_root()
    {
        var contentRoot = ContentRootResolver.Resolve();
        var provider = new ContentProvider(contentRoot);

        var cs = provider.ProgressionCurves.CraftingSkillScaling;
        cs.BaseTolerance.Should().BeApproximately(0.05, 1e-9);
        cs.TolerancePerSkillTier.Should().BeApproximately(0.02, 1e-9);
        cs.MaxTolerance.Should().BeApproximately(0.20, 1e-9);

        ProgressionCurvesAccessor.CraftingBaseTolerance.Should().BeApproximately(0.05, 1e-9);
    }

    [Fact]
    public void ContentProvider_missing_crafting_skill_scaling_block_falls_back_to_defaults()
    {
        var realRoot = ContentRootResolver.Resolve();
        using var tmp = new MirroredContentRoot(realRoot);

        var curvesPath = Path.Combine(tmp.Path, "progression-curves.json");
        var body = File.ReadAllText(curvesPath);
        var stripped = Regex.Replace(
            body,
            ",\\s*\"craftingSkillScaling\"\\s*:\\s*\\{[^}]*\\}",
            string.Empty,
            RegexOptions.Singleline);
        File.WriteAllText(curvesPath, stripped);

        var provider = new ContentProvider(tmp.Path);
        var cs = provider.ProgressionCurves.CraftingSkillScaling;
        cs.BaseTolerance.Should().BeApproximately(ProgressionCurvesAccessor.DefaultCraftingBaseTolerance, 1e-9);
        cs.TolerancePerSkillTier.Should().BeApproximately(ProgressionCurvesAccessor.DefaultCraftingTolerancePerSkillTier, 1e-9);
        cs.MaxTolerance.Should().BeApproximately(ProgressionCurvesAccessor.DefaultCraftingMaxTolerance, 1e-9);
    }

    private sealed class MirroredContentRoot : IDisposable
    {
        public string Path { get; }

        public MirroredContentRoot(string source)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "fm-cs-" + Guid.NewGuid().ToString("N"));
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
