using System.Text.RegularExpressions;
using FirstMud.GameServer.Services;
using FluentAssertions;

namespace FirstMud.Tests.Services;

/// <summary>
/// Guards the TEMPLATE_BLOCKLIST in DungeonMasterService: procgen must not emit
/// escort / deliver / courier / protect quest titles until a pickup-flow exists.
/// See DungeonMasterService.QuestTitleTemplates for rationale.
/// </summary>
public class DungeonMasterQuestTemplatesTests
{
    private static readonly Regex CarrierRegex = new(
        @"\b(escort|deliver|delivery|courier|protect|carry|transport|smuggle|ferry)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void QuestTitleTemplates_ContainsNoCarrierFlowTemplates()
    {
        foreach (var title in DungeonMasterService.QuestTitleTemplates)
        {
            CarrierRegex.IsMatch(title).Should().BeFalse(
                $"template '{title}' implies a pickup/carrier flow that procgen cannot satisfy");
        }
    }

    [Fact]
    public void QuestDescTemplates_ContainsNoCarrierFlowTemplates()
    {
        foreach (var desc in DungeonMasterService.QuestDescTemplates)
        {
            CarrierRegex.IsMatch(desc).Should().BeFalse(
                $"desc template '{desc}' implies a pickup/carrier flow that procgen cannot satisfy");
        }
    }

    [Fact]
    public void QuestTitleAndDescTemplates_AreIndexAligned()
    {
        // BuildQuestContent indexes both arrays in parallel; keep them equal length.
        DungeonMasterService.QuestTitleTemplates.Length
            .Should().Be(DungeonMasterService.QuestDescTemplates.Length);
    }

    [Fact]
    public void QuestTitleTemplates_HasAtLeastThreeSelfContainedTemplates()
    {
        // Below this, procgen variety collapses — flag if a future edit strips too many.
        DungeonMasterService.QuestTitleTemplates.Length.Should().BeGreaterThanOrEqualTo(3);
    }
}
