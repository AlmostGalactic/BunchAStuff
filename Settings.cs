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
        private static MelonPreferences_Entry<float> _gunDamage;
        private static MelonPreferences_Entry<float> _punchDamage;
        private static MelonPreferences_Entry<float> _punchForce;
        private static MelonPreferences_Entry<bool> _fightBack;
        private static MelonPreferences_Entry<bool> _showTeamTags;
        private static MelonPreferences_Entry<string> _customTeams;

        internal static void Create()
        {
            _prefs = MelonPreferences.CreateCategory("BunchAStuff", "Bunch-A-Stuff! settings");
            _gunDamage = _prefs.CreateEntry("GunDamage", 1f, "Gun damage",
                "How much the guns hurt. Below 1 they only bruise, above 1 they tear more away.", false, false, new ValueRange<float>(0.1f, 3f));
            _punchDamage = _prefs.CreateEntry("PunchDamage", 0.35f, "Punch damage",
                "How much each punch hurts in a fist fight.", false, false, new ValueRange<float>(0f, 1f));
            _punchForce = _prefs.CreateEntry("PunchForce", 18f, "Punch force",
                "How hard punches knock people back.", false, false, new ValueRange<float>(0f, 80f));
            _fightBack = _prefs.CreateEntry("FightBack", true, "People fight back",
                "Someone who gets picked on in a fight starts fighting too.");
            _showTeamTags = _prefs.CreateEntry("ShowTeamTags", true, "Show team names",
                "Show each person's team over their head.");
            // Teams the player made, as "Name|RRGGBB;Name|RRGGBB". Edited on the mod's page, not here.
            _customTeams = _prefs.CreateEntry("CustomTeams", string.Empty, "Custom teams", null, is_hidden: true);
            ModMenu.AddPreferencesPage(_prefs);
        }

        internal static float GunDamage => _gunDamage.Value;
        internal static float PunchDamage => _punchDamage.Value;
        internal static float PunchForce => _punchForce.Value;
        internal static bool FightBack => _fightBack.Value;
        internal static bool ShowTeamTags => _showTeamTags.Value;

        internal static IEnumerable<(string Name, Color Color)> LoadCustomTeams()
        {
            foreach (var part in (_customTeams.Value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = part.Split('|');
                if (bits.Length != 2 || string.IsNullOrWhiteSpace(bits[0]))
                    continue;
                yield return (bits[0].Trim(), ColorUtility.TryParseHtmlString("#" + bits[1].Trim(), out var color) ? color : Color.white);
            }
        }

        internal static void SaveCustomTeams(IEnumerable<(string Name, Color Color)> teams)
        {
            _customTeams.Value = string.Join(";", teams.Select(t => t.Name.Replace(";", "").Replace("|", "") + "|" + ColorUtility.ToHtmlStringRGB(t.Color)));
            _prefs.SaveToFile(false);
        }
    }
}
