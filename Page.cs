using System.Linq;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.UI;

namespace BunchAStuff
{
    /// <summary>The mod's page in the mod menu: fights, and making and managing teams.</summary>
    internal static class Page
    {
        private static ModMenuPage _page;
        private static string _newName = string.Empty;
        private static int _newColour = 4;

        internal static ModMenuPage Menu => _page;

        /// <summary>The name typed for a new team (the self-test reads it).</summary>
        internal static string NewTeamName => _newName;

        internal static void Create()
        {
            _page = ModMenu.AddPage("Bunch-A-Stuff!");
            Teams.Changed += Rebuild;
            Rebuild();
        }

        private static void Rebuild()
        {
            _page.Clear()
                .Header("Fights")
                .Label(() => Fights.Count == 0 ? "Nobody is fighting. Right-click someone and pick Fight nearest."
                    : $"{Fights.Count} {(Fights.Count == 1 ? "person is" : "people are")} fighting.")
                .Button("Everyone fights", () =>
                {
                    if (!GameState.InSandbox)
                        return;
                    int count = Fights.EveryoneFights();
                    Notifications.Show(count == 0 ? "Nobody could fight." : $"{count} people are fighting.");
                }).WithTooltip("Everyone who can stand goes for the nearest person who isn't on their team.")
                .Button("Stop all fights", Fights.StopAll)

                .Header("Teams")
                .Label("Teammates never fight or hit each other. Right-click someone and open Team to pick theirs.")
                .Button("Split everyone into teams", () =>
                {
                    if (!GameState.InSandbox)
                        return;
                    if (Teams.All.Count == 0)
                    {
                        Notifications.Warn("Make a team first.");
                        return;
                    }
                    Notifications.Show($"{Teams.SplitEveryone()} people split between {Teams.All.Count} teams.");
                }).WithTooltip("Deals everyone out between all the teams, as evenly as it can.")
                .Button("Take everyone off their teams", Teams.ClearMembers);

            foreach (var team in Teams.All)
            {
                var t = team;
                _page.Label(() => $"{t.Name}: {Count(t)}");
                _page.Button($"Delete {t.Name}", () => Teams.Remove(t));
            }

            _page.Header("Make a team")
                .TextField("Name", () => _newName, text => _newName = text, 20)
                .Choice("Colour", Teams.Colours.Select(c => c.Name).ToList(), () => _newColour, i => _newColour = i)
                .Button("Make the team", () =>
                {
                    var team = Teams.Add(_newName, Teams.Colours[_newColour].Color, out string why);
                    if (team == null)
                    {
                        Notifications.Warn(why);
                        return;
                    }
                    Notifications.Show($"Made the team {team.Name}.");
                    _newName = string.Empty;
                });
        }

        private static string Count(Team team)
        {
            int count = Teams.MembersOf(team).Count(c => c.IsLiving());
            return count == 1 ? "1 person" : $"{count} people";
        }
    }
}
