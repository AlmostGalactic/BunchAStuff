using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FruktSharedLibrary.UI;
using MelonLoader;
using MelonLoader.Preferences;
using UnityEngine;

namespace BunchAStuff
{
    /// <summary>The mod's settings, saved by MelonLoader and editable in the mod menu.</summary>
    internal static class Settings
    {
        private static MelonPreferences_Category _prefs;
        private static MelonPreferences_Entry<float> _explosionDamage;
        private static MelonPreferences_Entry<float> _punchDamage;
        private static MelonPreferences_Entry<float> _punchForce;
        private static MelonPreferences_Entry<bool> _fightBack;
        private static MelonPreferences_Entry<bool> _bruising;
        private static MelonPreferences_Entry<float> _bruiseSeconds;
        private static MelonPreferences_Entry<bool> _showTeamTags;
        private static MelonPreferences_Entry<string> _customTeams;
        private static MelonPreferences_Entry<bool> _teamsSeeded;

        internal static void Create()
        {
            _prefs = MelonPreferences.CreateCategory("BunchAStuff", "Bunch-A-Stuff! settings");
            _explosionDamage = _prefs.CreateEntry("ExplosionDamage", 1f, "Explosion damage",
                "How much the Tusk-40's blast hurts. Below 1 it only bruises, above 1 it tears more away.", false, false, new ValueRange<float>(0.1f, 3f));
            _punchDamage = _prefs.CreateEntry("PunchDamage", 0.35f, "Punch damage",
                "How much each punch hurts in a fist fight.", false, false, new ValueRange<float>(0f, 1f));
            _punchForce = _prefs.CreateEntry("PunchForce", 18f, "Punch force",
                "How hard punches knock people back.", false, false, new ValueRange<float>(0f, 80f));
            _fightBack = _prefs.CreateEntry("FightBack", true, "People fight back",
                "Someone who gets picked on in a fight starts fighting too.");
            _bruising = _prefs.CreateEntry("Bruising", true, "Bruises",
                "People bruise where they're punched or hit something hard.");
            _bruiseSeconds = _prefs.CreateEntry("BruiseMinutes", 20f, "Bruises last (minutes)",
                "How long a bruise takes to heal: red, then deep purple, then green and yellow, then gone.", false, false, new ValueRange<float>(1f, 120f));
            _showTeamTags = _prefs.CreateEntry("ShowTeamTags", true, "Show team names",
                "Show each person's team over their head.");
            // Teams the player made, as "Name|RRGGBB;Name|RRGGBB". Edited on the mod's page, not here.
            _customTeams = _prefs.CreateEntry("CustomTeams", string.Empty, "Teams", null, is_hidden: true);
            _teamsSeeded = _prefs.CreateEntry("TeamsSeeded", false, "Teams made", null, is_hidden: true);
            ModMenu.AddPreferencesPage(_prefs);
        }

        internal static float ExplosionDamage => _explosionDamage.Value;
        internal static float PunchDamage => _punchDamage.Value;
        internal static float PunchForce => _punchForce.Value;
        internal static bool FightBack => _fightBack.Value;
        internal static bool Bruising => _bruising.Value;
        internal static float BruiseSeconds => Mathf.Max(1f, _bruiseSeconds.Value) * 60f;
        internal static bool ShowTeamTags => _showTeamTags.Value;

        internal static IEnumerable<(string Name, Color Color)> LoadTeams()
        {
            foreach (var part in (_customTeams.Value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = part.Split('|');
                if (bits.Length != 2 || string.IsNullOrWhiteSpace(bits[0]))
                    continue;
                yield return (bits[0].Trim(), ColorUtility.TryParseHtmlString("#" + bits[1].Trim(), out var color) ? color : Color.white);
            }
        }

        /// <summary>False until the four starting teams have been made once.</summary>
        internal static bool TeamsSeeded => _teamsSeeded.Value;

        internal static void SaveTeams(IEnumerable<(string Name, Color Color)> teams)
        {
            _teamsSeeded.Value = true;
            _customTeams.Value = string.Join(";", teams.Select(t => t.Name.Replace(";", "").Replace("|", "") + "|" + ColorUtility.ToHtmlStringRGB(t.Color)));
            _prefs.SaveToFile(false);
        }
    }
}
