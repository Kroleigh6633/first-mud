using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.DialogueLint;
using FirstMud.DesignTools.Tools.EconomySim;
using FirstMud.DesignTools.Tools.EncounterSim;
using FirstMud.DesignTools.Tools.ProgressionSim;
using FirstMud.DesignTools.Tools.FactionState;
using FirstMud.DesignTools.Tools.ScenarioPlayer;
using FirstMud.DesignTools.Tools.PlaybookRunner;

namespace FirstMud.DesignTools;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0];
        var rest = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "scenario-player" => ScenarioPlayerCommand.Run(rest),
                "dialogue-lint"   => DialogueLintCommand.Run(rest),
                "faction-state"   => FactionStateCommand.Run(rest),
                "encounter-sim"   => EncounterSimCommand.Run(rest),
                "economy-sim"     => EconomySimCommand.Run(rest),
                "progression-sim" => ProgressionSimCommand.Run(rest),
                "playbook-runner" => PlaybookRunnerCommand.Run(rest),
                _                 => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            ConsolePretty.Error(ex.ToString());
            return 2;
        }
    }

    private static int Unknown(string cmd)
    {
        ConsolePretty.Error($"Unknown command '{cmd}'.");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        ConsolePretty.Header("FirstMud.DesignTools");
        Console.WriteLine("Usage: FirstMud.DesignTools <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  scenario-player   Step through a quest-chain spec.     [FULL]");
        Console.WriteLine("  dialogue-lint     Validate a dialogue tree.            [FULL]");
        Console.WriteLine("  faction-state     Render factions at a marker.         [scaffold]");
        Console.WriteLine("  encounter-sim     Simulate combat rolls.               [FULL]");
        Console.WriteLine("  economy-sim       Simulate N hours of play.            [scaffold]");
        Console.WriteLine("  progression-sim   Full progression curve sim.          [FULL]");
        Console.WriteLine("  playbook-runner   Run a balance playbook.              [FULL]");
        Console.WriteLine();
        Console.WriteLine("Each tool writes JSON + markdown under docs/design/sim-logs/.");
    }
}
