using System;
using System.Collections.Generic;
using System.Linq;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.UI;
using Il2CppLVA.Creatures;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using UnityEngine;

namespace BunchAStuff
{
    internal sealed class Team
    {
        internal string Name { get; }
        internal Color Color { get; }

        internal Team(string name, Color color)
        {
            Name = name;
            Color = color;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Teams for people. Teammates never fight each other and never hit each other. The first time the mod runs it
    /// makes four teams, and after that the player makes and deletes teams as they like on the mod's page. The list
    /// is saved.
    /// </summary>
    internal static class Teams
    {
        internal static readonly (string Name, Color Color)[] Colours =
        {
            ("red", new Color(0.95f, 0.25f, 0.2f)),
            ("blue", new Color(0.25f, 0.5f, 1f)),
            ("green", new Color(0.3f, 0.85f, 0.3f)),
            ("yellow", new Color(1f, 0.85f, 0.2f)),
            ("purple", new Color(0.7f, 0.35f, 1f)),
            ("orange", new Color(1f, 0.55f, 0.1f)),
            ("cyan", new Color(0.2f, 0.9f, 0.95f)),
            ("pink", new Color(1f, 0.45f, 0.75f)),
            ("white", new Color(0.95f, 0.95f, 0.95f)),
            ("black", new Color(0.15f, 0.15f, 0.15f)),
        };

        private static readonly List<Team> TeamList = new();
        private sealed class Member
        {
            public AbstractCreature Creature;
            public Team Team;
            public WorldLabel Tag;
        }

        private static readonly Dictionary<IntPtr, Member> Members = new();
        private static ContextMenuGroup _menu;

        internal static IReadOnlyList<Team> All => TeamList;

        /// <summary>The teams changed: one was made or deleted, or someone joined or left one.</summary>
        internal static event Action Changed;

        internal static void Create()
        {
            if (!Settings.TeamsSeeded)
            {
                for (int i = 0; i < 4; i++)
                    TeamList.Add(new Team(char.ToUpperInvariant(Colours[i].Name[0]) + Colours[i].Name.Substring(1), Colours[i].Color));
            }
            foreach (var (name, color) in Settings.LoadTeams())
            {
                if (Find(name) == null)
                    TeamList.Add(new Team(name, color));
            }
            if (!Settings.TeamsSeeded)
                Settings.SaveTeams(TeamList.Select(t => (t.Name, t.Color)));
            BuildMenu();
        }

        internal static Team Find(string name) => TeamList.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Makes a new team. Null, with the reason in <paramref name="why"/>, if the name is empty or taken.</summary>
        internal static Team Add(string name, Color color, out string why)
        {
            name = name?.Trim() ?? string.Empty;
            why = name.Length == 0 ? "Give the team a name first."
                : Find(name) != null ? $"There's already a team called {name}."
                : null;
            if (why != null)
                return null;
            var team = new Team(name, color);
            TeamList.Add(team);
            SaveAndRefresh();
            return team;
        }

        /// <summary>Deletes a team. Its people are left without one.</summary>
        internal static bool Remove(Team team)
        {
            if (team == null || !TeamList.Remove(team))
                return false;
            foreach (var key in Members.Where(m => m.Value.Team == team).Select(m => m.Key).ToList())
                Leave(key);
            SaveAndRefresh();
            return true;
        }

        // ------------------------------------------------------------ members

        internal static Team Of(AbstractCreature creature)
        {
            if (creature == null || !Members.TryGetValue(creature.Pointer, out var member))
                return null;
            if (!member.Creature.IsValid())
            {
                Leave(creature.Pointer);
                return null;
            }
            return member.Team;
        }

        /// <summary>Puts someone on a team, or takes them off theirs with null.</summary>
        internal static void Join(AbstractCreature creature, Team team)
        {
            if (creature == null)
                return;
            if (team == null)
                Leave(creature.Pointer);
            else
                Put(creature, team);
            Changed?.Invoke();
        }

        /// <summary>True when both are on the same team. People without a team have no allies.</summary>
        internal static bool AreAllies(AbstractCreature a, AbstractCreature b)
        {
            var team = Of(a);
            return team != null && team == Of(b);
        }

        internal static List<AbstractCreature> MembersOf(Team team)
        {
            Prune();
            return Members.Values.Where(m => m.Team == team).Select(m => m.Creature).ToList();
        }

        /// <summary>Splits every living person between all the teams, as evenly as it can.</summary>
        internal static int SplitEveryone()
        {
            if (TeamList.Count == 0)
                return 0;
            var people = Creatures.Humans.Select(h => (AbstractCreature)h).Where(h => h.IsLiving())
                .OrderBy(_ => UnityEngine.Random.value).ToList();
            // Dealt out like cards, so the teams end up as even as they can be: no team has more than one extra.
            // The teams are shuffled too, so the extra people don't always land on the first teams.
            var order = TeamList.OrderBy(_ => UnityEngine.Random.value).ToList();
            for (int i = 0; i < people.Count; i++)
                Put(people[i], order[i % order.Count]);
            Changed?.Invoke();
            return people.Count;
        }

        internal static void ClearMembers()
        {
            foreach (var key in Members.Keys.ToList())
                Leave(key);
            Changed?.Invoke();
        }

        private static void Prune()
        {
            foreach (var key in Members.Where(m => !m.Value.Creature.IsValid()).Select(m => m.Key).ToList())
                Leave(key);
        }

        private static void Put(AbstractCreature creature, Team team)
        {
            if (!Members.TryGetValue(creature.Pointer, out var member))
                Members[creature.Pointer] = member = new Member { Creature = creature };
            member.Team = team;
            if (member.Tag == null || !member.Tag.Exists)
            {
                var head = creature.GetLimb(HumanoidNodeTagValue.Head);
                member.Tag = head != null
                    ? WorldLabels.Add(head.GetMovingTransform(), team.Name, team.Color, Vector3.up * 0.45f)
                    : WorldLabels.Add(() => creature.GetPosition(), team.Name, team.Color, Vector3.up * 1.1f);
            }
            member.Tag.Text = team.Name;
        }

        private static void Leave(IntPtr key)
        {
            if (Members.TryGetValue(key, out var member))
                member.Tag?.Remove();
            Members.Remove(key);
        }

        // ------------------------------------------------------------ the right-click menu

        private static void BuildMenu()
        {
            _menu?.Remove();
            _menu = ContextMenus.AddCreatureGroup("Team", c => c.IsHuman(), priority: 480);
            foreach (var team in TeamList)
            {
                var t = team;
                _menu.AddAction(ctx => (Of(ctx.Creature) == t ? "> " : "") + t.Name, ctx => Join(ctx.Creature, t));
            }
            _menu.AddAction(ctx => (Of(ctx.Creature) == null ? "> " : "") + "No team", ctx => Join(ctx.Creature, null));
        }

        private static void SaveAndRefresh()
        {
            Settings.SaveTeams(TeamList.Select(t => (t.Name, t.Color)));
            BuildMenu();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ name tags

        /// <summary>The team names over people's heads: hidden when the setting is off, dimmed once they're dead.</summary>
        internal static void Update()
        {
            if (Members.Count == 0)
                return;
            Prune();
            foreach (var member in Members.Values)
            {
                if (member.Tag == null)
                    continue;
                bool living = member.Creature.IsLiving();
                member.Tag.Visible = Settings.ShowTeamTags;
                member.Tag.Color = living ? member.Team.Color : Color.Lerp(member.Team.Color, Color.gray, 0.6f);
            }
        }

        /// <summary>Someone's name tag (for the self-test).</summary>
        internal static WorldLabel TagOf(AbstractCreature creature)
            => creature != null && Members.TryGetValue(creature.Pointer, out var member) ? member.Tag : null;
    }
}
