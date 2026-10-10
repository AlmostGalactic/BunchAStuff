using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FruktSharedLibrary.Core;
using FruktSharedLibrary.Entities;
using FruktSharedLibrary.Gameplay;
using FruktSharedLibrary.Interop;
using FruktSharedLibrary.UI;
using Il2CppLVA.Creatures;
using Il2CppLVA.Limbs;
using Il2CppLVA.NodesHierarchy.Benchmark.Variants;
using Il2CppSpawnables.Misc;
using Il2CppVoxelMeshGeneration;
using Il2CppVoxelMeshGeneration.Painting;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace BunchAStuff
{
    /// <summary>
    /// The Clothed Human Spawner: a copy of the game's Human Spawner whose people come out dressed in one of the
    /// outfits in <see cref="Outfits"/>. Each piece of clothing is a voxel model fitted round its body part and
    /// carried by it. Where the body under it is shot, cut or blown away, the cloth there tears out, leaving a ragged
    /// edge stained with blood; a limb that's cut off takes its piece with it, and a hat falls off a badly hurt head.
    /// </summary>
    internal static unsafe class Clothes
    {
        private sealed class Piece
        {
            // The body part it's on. A piece cut off a body part may not be one, and then this is null.
            internal AbstractLimb Limb;
            // The voxels it sits on: the body part's, or the cut-off piece's.
            internal VoxelMesh Body;
            internal IntPtr BodyPointer;
            // Set when its body changed and it needs checking against it.
            internal bool Due;
            internal Dictionary<char, Color> Colours;
            // The voxel meshes the person was made of when dressed. A cut-off piece is a new one.
            internal HashSet<IntPtr> Known;
            internal Transform On;
            internal GameObject Object;
            internal MeshFilter Filter;
            internal Voxels V;
            internal Fit Fit;
            internal Vector3 Min, Cell;
            // The body cell each cloth voxel sits on. The cloth tears where that cell is gone.
            internal readonly Dictionary<(int, int, int), (int, int, int)> Anchors = new();
            internal bool IsHat;
            internal float Wholeness;
            internal bool Stained;
            internal Vector3Int Grid;
            internal int Top;
            internal string Name;
            // When cloth first lost its body, waiting a moment for the piece it went with to appear.
            internal float Lost;
            internal float NextLook;
            internal bool CutOff;
        }

        // A body's voxels as cells of a piece's grid: one flag per cell, the body itself only (not the cloth round it).
        private sealed class BodyGrid
        {
            internal readonly int X, Y, Z;
            internal readonly bool[] Cells;
            internal int Count;

            internal BodyGrid(Fit fit)
            {
                X = fit.X;
                Y = fit.Y;
                Z = fit.Z;
                Cells = new bool[X * Y * Z];
            }

            internal bool Has(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < X && y < Y && z < Z && Cells[(z * Y + y) * X + x];

            internal bool Has((int X, int Y, int Z) cell) => Has(cell.X, cell.Y, cell.Z);

            internal void Add(int x, int y, int z)
            {
                int i = (z * Y + y) * X + x;
                if (!Cells[i])
                {
                    Cells[i] = true;
                    Count++;
                }
            }
        }

        private static bool Wears(Piece piece, AbstractLimb limb) => piece.Limb != null && limb != null && piece.Limb.Pointer == limb.Pointer;

        private sealed class Job
        {
            internal AbstractLimb Limb;
            internal HumanoidNodeTagValue Part;
            internal Outfits.Outfit Outfit;
            internal Action<Fit> Make;
            internal bool IsHat;
            internal float Waited;
            internal HashSet<IntPtr> Known;
        }

        // The size of a cloth voxel: a little finer than the body's own, so details read and the cloth stays thin.
        private const float Unit = 1f / 60f;
        private const float LookEvery = 0.25f;
        // A spawner that shows up this soon after the Clothed Human Spawner was in hand is one of its.
        private const float HeldGrace = 2f;
        private const float BirthDistance = 8f;
        // How long cloth over a cut waits for the cut-off piece to appear before it's torn instead.
        private const float WaitForCutOff = 0.3f;
        private const double BudgetMs = 2.5;

        private static ModTool _spawner;
        private static readonly HashSet<IntPtr> Seen = new();
        private static readonly List<HumanSpawner> Clothed = new();
        private static readonly List<Job> Jobs = new();
        private static readonly List<(AbstractCreature Creature, float At)> Waiting = new();
        private static readonly List<Piece> Pieces = new();
        // Voxel meshes the game changed since the last frame, and when meshes were last split in two.
        private static readonly HashSet<IntPtr> Changed = new();
        private static readonly Dictionary<IntPtr, float> SplitAt = new();
        private static bool _changesHooked, _splitsHooked;
        private static int _sweep;
        // Pieces checked each frame by the slow sweep, which tidies up and catches anything the hooks missed.
        private const int SweepPerFrame = 6;
        private const float SweepEvery = 1f;
        // The scene's voxel meshes and the pieces' positions, found at most once a frame and only when needed.
        private static List<VoxelMesh> _meshes;
        private static int _meshesFrame = -1, _positionsFrame = -1;
        private static readonly Dictionary<Piece, Vector3> Positions = new();
        private static readonly Stopwatch Clock = new();
        private static float _heldAt = -10f, _look, _stain;
        private const float RepaintStainsEvery = 0.25f;
        private static int _turn;

        internal static ModTool Spawner => _spawner;

        /// <summary>The game's blood stamps that have landed on clothes (for the self-test).</summary>
        internal static int BloodStamps { get; private set; }

        /// <summary>Every paint stamp seen, on clothes or not (for the self-test).</summary>
        internal static int AllStamps { get; private set; }

        /// <summary>Pieces of clothing that went with a cut-off piece of body (for the self-test).</summary>
        internal static int CutOff { get; private set; }

        /// <summary>Cloth voxels on cut-off pieces that aren't on the person any more (for the self-test).</summary>
        internal static int ClothCutOff => Pieces.Where(p => p.CutOff).Sum(p => p.V.Count);

        /// <summary>Pieces of clothing being worn (for the self-test).</summary>
        internal static int Worn => Pieces.Count;

        /// <summary>Whether anyone handed out is still being dressed (for the self-test).</summary>
        internal static bool Busy => Jobs.Count > 0 || Waiting.Count > 0;

        /// <summary>How many cloth voxels a limb's piece has left (for the self-test).</summary>
        internal static int ClothOn(AbstractLimb limb) => limb == null ? 0 : Pieces.Where(p => !p.IsHat && Wears(p, limb)).Sum(p => p.V.Count);

        /// <summary>Whether someone's hat is still on (for the self-test).</summary>
        internal static bool HatOn(AbstractCreature creature)
            => creature != null && Pieces.Any(p => p.IsHat && p.Limb != null && p.Limb.Exists() && p.Limb.GetCreature()?.Pointer == creature.Pointer);

        internal static void Create()
        {
            _spawner = Inventory.AddCopy("Clothed Human Spawner", "Human")
                .WithDescription("Like the Human Spawner, but the people it makes come out dressed: office workers, " +
                                 "builders, doctors, police officers, chefs, footballers, farmers, tourists and more.");
            GameEvents.CreatureSpawned += Spawned;
            PatchBlood();
            // Work clothes, and a change of everyday ones, from the right-click menu.
            var menu = ContextMenus.AddCreatureGroup("Clothes", c => c.IsHuman(), priority: 470);
            var work = menu.AddGroup("Work clothes");
            foreach (var name in Outfits.JobNames)
            {
                string outfit = name;
                work.AddCreatureAction(outfit, c => Dress(c, Outfits.Named(outfit)));
            }
            var everyday = menu.AddGroup("Everyday clothes");
            everyday.AddCreatureAction("Something else", c => Dress(c));
            foreach (var name in Outfits.EverydayNames)
            {
                string outfit = name;
                everyday.AddCreatureAction(outfit, c => Dress(c, Outfits.Named(outfit)));
            }
            menu.AddCreatureAction("Take them off", Strip, c => Pieces.Any(p => p.Limb != null && p.Limb.Exists() && p.Limb.GetCreature()?.Pointer == c.Pointer));
        }

        /// <summary>Time spent in <see cref="Update"/>: the total, the worst frame, and frames counted (for the self-test).</summary>
        internal static double SpentMs, WorstMs;
        internal static int SpentFrames;
        internal static bool RawVoxels => _rawWorks == true;
        internal static bool Hooked => _changesHooked && _splitsHooked;
        private static readonly Stopwatch Spent = new();

        internal static void Update()
        {
            Spent.Restart();
            UpdateInner();
            double ms = Spent.Elapsed.TotalMilliseconds;
            SpentMs += ms;
            WorstMs = Math.Max(WorstMs, ms);
            SpentFrames++;
        }

        private static void UpdateInner()
        {
            if (_spawner != null && _spawner.IsHeld)
                _heldAt = Time.time;
            if ((_look += Time.deltaTime) >= LookEvery)
            {
                _look = 0f;
                FindSpawners();
            }
            DressWaiting();
            Clock.Restart();
            if (Jobs.Count > 0)
                MakeSome();
            if (Pieces.Count > 0)
                Watch();
            if ((_stain += Time.deltaTime) >= RepaintStainsEvery)
            {
                _stain = 0f;
                foreach (var piece in Pieces)
                {
                    if (!piece.Stained || Clock.Elapsed.TotalMilliseconds >= BudgetMs)
                        continue;
                    piece.Stained = false;
                    if (piece.Object.Exists())
                        Rebuild(piece);
                }
            }
        }

        internal static void Reset()
        {
            Seen.Clear();
            Clothed.Clear();
            Jobs.Clear();
            Waiting.Clear();
            Pieces.Clear();
            Changed.Clear();
            SplitAt.Clear();
        }

        /// <summary>True if clothes cover a limb (bruises don't go under them).</summary>
        internal static bool Covers(AbstractLimb limb, Vector3 point)
            => limb.Exists() && Pieces.Any(p => !p.IsHat && Wears(p, limb) && p.V.Count > 0);

        /// <summary>Dresses someone in a random outfit, or the one given.</summary>
        internal static void Dress(AbstractCreature creature, Outfits.Outfit outfit = null)
        {
            if (creature == null || !creature.IsHuman())
                return;
            outfit ??= Outfits.Any();
            // One outfit at a time: whatever they had on, or were about to be given, comes off first.
            Strip(creature);
            var limbs = creature.GetLimbs();
            var known = new HashSet<IntPtr>(limbs.Select(l => l.GetVoxelMesh()).Where(m => m != null).Select(m => m.Pointer));
            foreach (var limb in limbs)
            {
                var part = limb.GetHumanPart();
                if (part == null)
                    continue;
                if (outfit.Pieces.TryGetValue(part.Value, out var make))
                    Jobs.Add(new Job { Limb = limb, Part = part.Value, Outfit = outfit, Make = make, Known = known });
                if (part.Value == HumanoidNodeTagValue.Head && outfit.Hat != null)
                    Jobs.Add(new Job { Limb = limb, Part = part.Value, Outfit = outfit, Make = outfit.Hat, IsHat = true, Known = known });
            }
        }

        // ------------------------------------------------------------ spawners

        // Spawners the player puts down while holding the Clothed Human Spawner are its. The one in their hand
        // counts too, and it stays clothed if it becomes the one that's put down.
        private static void FindSpawners()
        {
            Clothed.RemoveAll(s => !s.Exists());
            bool ours = Time.time - _heldAt < HeldGrace;
            foreach (var spawner in GameServices.FindObjects<HumanSpawner>())
            {
                if (spawner.Exists() && Seen.Add(spawner.Pointer) && ours)
                    Clothed.Add(spawner);
            }
        }

        // Someone made by it, or born at one of its spawners, gets dressed once their body is put together.
        private static void Spawned(AbstractCreature creature)
        {
            if (creature == null || !creature.IsHuman())
                return;
            // The game reuses bodies: a whole new person must not still be wearing someone else's clothes. A limb
            // that's cut off becomes a creature of its own too, so only a complete body counts as new.
            if (creature.GetLimbCount() >= 15)
                Strip(creature);
            // Made with the one in the player's hand: it puts people where it's aimed, away from itself.
            if (Time.time - _heldAt < HeldGrace)
            {
                Wait(creature);
                return;
            }
            if (Clothed.Count == 0)
                return;
            FindSpawners();
            var at = creature.GetPosition();
            HumanSpawner nearest = null;
            float best = BirthDistance;
            foreach (var spawner in GameServices.FindObjects<HumanSpawner>())
            {
                float distance = spawner.Exists() ? Vector3.Distance(spawner.transform.position, at) : float.MaxValue;
                if (distance < best)
                {
                    best = distance;
                    nearest = spawner;
                }
            }
            if (nearest != null && Clothed.Any(s => s.Pointer == nearest.Pointer))
                Wait(creature);
        }

        private static void Wait(AbstractCreature creature)
        {
            if (!Waiting.Any(w => w.Creature.Pointer == creature.Pointer))
                Waiting.Add((creature, Time.time + 0.3f));
        }

        private static void DressWaiting()
        {
            for (int i = Waiting.Count - 1; i >= 0; i--)
            {
                var (creature, at) = Waiting[i];
                if (Time.time < at)
                    continue;
                if (!creature.IsValid() || Time.time > at + 5f)
                {
                    Waiting.RemoveAt(i);
                    continue;
                }
                if (creature.GetLimbCount() < 10)
                    continue;
                Waiting.RemoveAt(i);
                if (!IsDressed(creature))
                    Dress(creature);
            }
        }

        private static bool IsDressed(AbstractCreature creature)
        {
            var limbs = new HashSet<IntPtr>(creature.GetLimbs().Select(l => l.Pointer));
            return Jobs.Any(j => limbs.Contains(j.Limb.Pointer)) || Pieces.Any(p => p.Limb != null && limbs.Contains(p.Limb.Pointer));
        }

        /// <summary>Takes every piece of clothing off someone.</summary>
        internal static void Strip(AbstractCreature creature)
        {
            if (creature == null)
                return;
            try
            {
                foreach (var limb in creature.GetLimbs())
                {
                    var on = limb.GetMovingTransform();
                    if (!on.Exists())
                        continue;
                    for (int i = on.childCount - 1; i >= 0; i--)
                    {
                        var child = on.GetChild(i);
                        if (child.name.StartsWith(Tag, StringComparison.Ordinal))
                            Object.Destroy(child.gameObject);
                    }
                    Jobs.RemoveAll(j => j.Limb.Pointer == limb.Pointer);
                    Pieces.RemoveAll(p => Wears(p, limb) && p.Object.Exists() && p.Object.transform.parent == on);
                }
            }
            catch (Exception e)
            {
                FruktLog.Debug("Taking clothes off failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------ making

        // Every piece's object is named starting with this, so a reused body can be found still wearing one.
        private const string Tag = "Clothes: ";

        private static void MakeSome()
        {
            for (int i = 0; i < Jobs.Count && Clock.Elapsed.TotalMilliseconds < BudgetMs;)
            {
                var job = Jobs[i];
                if (!job.Limb.Exists() || job.Waited > 5f)
                {
                    Jobs.RemoveAt(i);
                    continue;
                }
                Piece piece = null;
                try
                {
                    piece = Make(job);
                }
                catch (Exception e)
                {
                    FruktLog.Debug($"Making {job.Outfit.Name}'s {job.Part} failed: {e.Message}");
                    job.Waited = 99f;
                }
                if (piece == null)
                {
                    job.Waited += Time.deltaTime;
                    i++;
                    continue;
                }
                Pieces.Add(piece);
                Jobs.RemoveAt(i);
            }
        }

        // Fits a piece round the body part: the part's size sets how many cloth voxels go across it, and the piece
        // is stretched a little so they line up with its edges.
        private static Piece Make(Job job)
        {
            var on = job.Limb.GetMovingTransform();
            if (!on.Exists() || !BodyBounds(job.Limb, on, out var min, out var max))
                return null;
            var size = max - min;
            int nx = Math.Max(2, Mathf.RoundToInt(size.x / Unit)), ny = Math.Max(2, Mathf.RoundToInt(size.y / Unit)), nz = Math.Max(2, Mathf.RoundToInt(size.z / Unit));
            var cell = new Vector3(size.x / nx, size.y / ny, size.z / nz);
            var voxels = new Voxels();
            foreach (var colour in job.Outfit.Colours)
                voxels.Key(colour.Key, colour.Value);
            var fit = new Fit(voxels, nx, ny, nz);
            job.Make(fit);
            if (voxels.Count == 0)
                return new Piece { Limb = job.Limb, On = on, V = voxels, Fit = fit, IsHat = job.IsHat, Colours = job.Outfit.Colours };
            string name = $"{Tag}{job.Outfit.Name} {(job.IsHat ? "hat" : job.Part.ToString())}";
            var piece = new Piece
            {
                Limb = job.Limb,
                Body = job.Limb.GetVoxelMesh(),
                BodyPointer = job.Limb.GetVoxelMesh().Pointer,
                Colours = job.Outfit.Colours,
                Known = job.Known,
                On = on,
                V = voxels,
                Fit = fit,
                Min = min,
                Cell = cell,
                IsHat = job.IsHat,
                Name = name,
            };
            var go = new GameObject(name);
            go.transform.SetParent(on, false);
            go.transform.localPosition = min + cell * 0.5f;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = cell / Voxels.Size;
            piece.Object = go;
            var body = BodyCellsOf(piece, piece.Body);
            piece.Wholeness = job.Limb.GetWholeness();
            piece.Grid = GridOf(piece.Body);
            piece.NextLook = Time.time + SweepEvery;
            piece.Top = TopOf(fit, body);
            // Cloth with no body under it (where the part isn't a plain block) is trimmed off before it's seen.
            if (!job.IsHat)
                Anchor(piece, body);
            // Faces against the body itself are never seen; faces over a gap in it are.
            voxels.HideWhere(body.Has);
            piece.Filter = go.AddComponent<MeshFilter>();
            piece.Filter.sharedMesh = voxels.BuildMesh(name);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = voxels.BuildMaterial(name);
            return piece;
        }

        // ------------------------------------------------------------ blood

        // The game's blood lands on the body two ways: painted onto the skin through the paint service, and as drops
        // that leave decals where they hit. Both would be hidden under the clothes, so each is caught here and stains
        // the cloth over the same spot instead.
        private static void PatchBlood()
        {
            var harmony = new HarmonyLib.Harmony("BunchAStuff.Clothes");
            bool Patch(Type type, string method, Type[] arguments, string postfix)
            {
                try
                {
                    var original = HarmonyLib.AccessTools.Method(type, method, arguments);
                    harmony.Patch(original, postfix: new HarmonyLib.HarmonyMethod(typeof(Clothes), postfix));
                    return true;
                }
                catch (Exception e)
                {
                    FruktLog.Warning($"Clothes can't follow {type.Name}.{method}: {e.Message}");
                    return false;
                }
            }
            Patch(typeof(VoxelMeshPaintService), nameof(VoxelMeshPaintService.TryStamp), null, nameof(Painted));
            // Knowing which bodies changed, and which were split in two, saves looking at every piece every frame.
            _changesHooked = Patch(typeof(VoxelMesh), nameof(VoxelMesh.SyncJobsVoxelsData), null, nameof(VoxelsChanged))
                             | Patch(typeof(VoxelMesh), nameof(VoxelMesh.MorphMesh), null, nameof(VoxelsChanged));
            _splitsHooked = Patch(typeof(Il2CppVoxelMeshGeneration.Separation.Performing.SeparationPerformer), "MorphAncestor", null, nameof(BodySplit))
                            | Patch(typeof(Il2CppVoxelMeshGeneration.Separation.Performing.SeparationPerformer), "CreateNewMeshes", null, nameof(BodySplit));
            Patch(typeof(Il2CppVFX.Blood.BloodDecalsPool), nameof(Il2CppVFX.Blood.BloodDecalsPool.TryDrawDecal),
                new[] { typeof(Vector3), typeof(Vector3) }, nameof(Dropped));
        }

        private static void VoxelsChanged(VoxelMesh __instance)
        {
            if (Pieces.Count > 0 && __instance != null)
                Changed.Add(__instance.Pointer);
        }

        private static void BodySplit(Il2CppVoxelMeshGeneration.Separation.Performing.SeparationPerformer __instance)
        {
            var mesh = __instance?.m_assignedMesh;
            if (Pieces.Count > 0 && mesh != null)
            {
                SplitAt[mesh.Pointer] = Time.time;
                Changed.Add(mesh.Pointer);
            }
        }

        private static void Painted(Collider target, Vector3 worldPosition, float radius, float opacity)
        {
            AllStamps++;
            if (Pieces.Count == 0 || target == null)
                return;
            try
            {
                var limb = Creatures.LimbFromCollider(target);
                if (limb != null)
                    Stain(limb, worldPosition, radius, opacity);
            }
            catch (Exception e)
            {
                FruktLog.Debug("Staining clothes failed: " + e.Message);
            }
        }

        // A drop of blood: whichever clothes are right there get a small stain.
        private static void Dropped(Vector3 worldPosition)
        {
            AllStamps++;
            if (Pieces.Count == 0)
                return;
            try
            {
                if (_positionsFrame != Time.frameCount)
                {
                    _positionsFrame = Time.frameCount;
                    Positions.Clear();
                    foreach (var piece in Pieces)
                    {
                        if (!piece.IsHat && piece.Object.Exists())
                            Positions[piece] = piece.Object.transform.position;
                    }
                }
                foreach (var (piece, position) in Positions)
                {
                    // No body part is much more than a metre across.
                    if ((position - worldPosition).sqrMagnitude > 1f || !piece.Object.Exists())
                        continue;
                    var at = CellAt(piece, worldPosition);
                    // A few cloth voxels of margin round the body part.
                    if (at.x < -4f || at.y < -4f || at.z < -4f || at.x > piece.Fit.X + 3f || at.y > piece.Fit.Y + 3f || at.z > piece.Fit.Z + 3f)
                        continue;
                    Stain(piece, worldPosition, 0.035f, 1f);
                }
            }
            catch (Exception e)
            {
                FruktLog.Debug("Staining clothes failed: " + e.Message);
            }
        }

        /// <summary>Stains the cloth round a point with blood, as a splash of that size.</summary>
        internal static void Stain(AbstractLimb limb, Vector3 point, float radius, float opacity = 1f)
        {
            foreach (var piece in Pieces)
            {
                if (!piece.IsHat && Wears(piece, limb) && piece.Object.Exists())
                    Stain(piece, point, radius, opacity);
            }
        }

        // The cloth voxels in the splash turn red, darker in the middle, with a ragged edge. Weak stamps (drips and
        // smears) stain less of it.
        private static void Stain(Piece piece, Vector3 point, float radius, float opacity)
        {
            if (radius <= 0f || opacity <= 0.02f)
                return;
            // In cloth voxels, with the distance worked out in metres.
            var at = CellAt(piece, point);
            // The cloth sits a voxel out from the skin the blood was painted on.
            float reach = radius + Mathf.Max(piece.Cell.x, Mathf.Max(piece.Cell.y, piece.Cell.z));
            int x0 = Mathf.FloorToInt(at.x - reach / piece.Cell.x), x1 = Mathf.CeilToInt(at.x + reach / piece.Cell.x);
            int y0 = Mathf.FloorToInt(at.y - reach / piece.Cell.y), y1 = Mathf.CeilToInt(at.y + reach / piece.Cell.y);
            int z0 = Mathf.FloorToInt(at.z - reach / piece.Cell.z), z1 = Mathf.CeilToInt(at.z + reach / piece.Cell.z);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 20000)
                return;
            float strength = Mathf.Clamp01(opacity * 1.5f);
            bool any = false;
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                char key = piece.V.At(x, y, z);
                if (key == ' ' || key == Outfits.DarkBlood)
                    continue;
                var offset = Vector3.Scale(new Vector3(x, y, z) - at, piece.Cell);
                float d = offset.magnitude / reach;
                if (d > 1f || Random.value > strength * (1.15f - d))
                    continue;
                piece.V.Paint(x, x, y, y, z, z, key == Outfits.Blood || d < 0.35f ? Outfits.DarkBlood : Outfits.Blood);
                any = true;
            }
            if (any)
            {
                piece.Stained = true;
                BloodStamps++;
            }
        }

        // ------------------------------------------------------------ tearing

        // Pieces whose body the game changed this frame are checked against it, within the time budget. A slow sweep
        // also looks at a few pieces each frame: it tidies away pieces whose body is gone, and checks each piece's
        // body once a second in case a change was missed (or the hooks couldn't be put in).
        private static void Watch()
        {
            if (Changed.Count > 0)
            {
                foreach (var piece in Pieces)
                {
                    if (Changed.Contains(piece.BodyPointer))
                        piece.Due = true;
                }
                Changed.Clear();
            }
            for (int n = 0; n < SweepPerFrame && Pieces.Count > 0; n++)
            {
                _sweep = (_sweep + 1) % Pieces.Count;
                var piece = Pieces[_sweep];
                if (!piece.Object.Exists() || piece.Body == null || !piece.Body.Exists() || (piece.Limb != null && !piece.Limb.Exists()))
                {
                    if (piece.Object.Exists())
                        Object.Destroy(piece.Object);
                    Pieces.RemoveAt(_sweep);
                    continue;
                }
                if (Time.time < piece.NextLook)
                    continue;
                piece.NextLook = Time.time + SweepEvery;
                if (piece.Limb == null || piece.Limb.GetWholeness() != piece.Wholeness)
                    piece.Due = true;
            }
            for (int i = 0; i < Pieces.Count; i++)
            {
                var piece = Pieces[i];
                if (!piece.Due && piece.Lost <= 0f)
                    continue;
                if (Clock.Elapsed.TotalMilliseconds >= BudgetMs)
                    break;
                piece.Due = false;
                if (!piece.Object.Exists() || piece.Body == null || !piece.Body.Exists())
                    continue;
                if (Check(piece))
                {
                    Pieces.RemoveAt(i);
                    i--;
                }
            }
        }

        // Checks a piece against its body. Returns true if the piece is gone.
        private static bool Check(Piece piece)
        {
            float wholeness = piece.Limb != null ? piece.Limb.GetWholeness() : 0f;
            piece.Wholeness = wholeness;
            var body = BodyCellsOf(piece, piece.Body);
            if (piece.IsHat)
            {
                // Knocked off when the top of the head is shot away, or the head is badly hurt anywhere.
                if (TopOf(piece.Fit, body) >= piece.Top * 0.8f && wholeness >= 85f)
                    return false;
                Drop(piece);
                return true;
            }
            if (body.Count == 0 && piece.Limb == null)
            {
                Object.Destroy(piece.Object);
                return true;
            }
            if (Tear(piece, body))
            {
                piece.V.HideWhere(body.Has);
                Rebuild(piece);
            }
            return false;
        }

        // How much of the top of the head is left, under a hat.
        private static int TopOf(Fit fit, BodyGrid body)
        {
            int count = 0;
            for (int x = 0; x < fit.X; x++)
            for (int z = 0; z < fit.Z; z++)
            {
                if (body.Has(x, fit.Y - 1, z) || body.Has(x, fit.Y - 2, z))
                    count++;
            }
            return count;
        }

        // Finds the body cell under each cloth voxel: the one it sits on, or the nearest one next to it. Cloth with no
        // body under it at all (where the part isn't a plain block) is trimmed off before it's seen.
        private static void Anchor(Piece piece, BodyGrid body)
        {
            var fit = piece.Fit;
            var bare = new List<(int X, int Y, int Z)>();
            foreach (var cell in piece.V.Cells)
            {
                int ax = Mathf.Clamp(cell.X, 0, fit.X - 1), ay = Mathf.Clamp(cell.Y, 0, fit.Y - 1), az = Mathf.Clamp(cell.Z, 0, fit.Z - 1);
                (int, int, int)? found = body.Has(ax, ay, az) ? (ax, ay, az) : null;
                for (int d = 0; found == null && d < 27; d++)
                {
                    var next = (ax + d % 3 - 1, ay + d / 3 % 3 - 1, az + d / 9 - 1);
                    if (body.Has(next))
                        found = next;
                }
                if (found.HasValue)
                    piece.Anchors[cell] = found.Value;
                else
                    bare.Add(cell);
            }
            foreach (var (x, y, z) in bare)
                piece.V.Clear(x, y, z);
        }

        // The cloth whose body cell is gone. Where the body was cut and the piece came away, the cloth over it goes with
        // the piece; where it was shot or blown away, the cloth tears: the hole's edge frays (more voxels come away
        // round it) and is stained with blood. Returns whether anything changed.
        private static bool Tear(Piece piece, BodyGrid body)
        {
            var gone = new List<(int X, int Y, int Z)>();
            foreach (var cell in piece.V.Cells)
            {
                if (!piece.Anchors.TryGetValue(cell, out var under) || !body.Has(under))
                    gone.Add(cell);
            }
            if (gone.Count == 0)
            {
                piece.Lost = 0f;
                return false;
            }
            // A shot or a blow only takes voxels away; a cut splits the body in two. Only then is there a cut-off
            // piece to look for, and it can take a moment to appear.
            bool split = !_splitsHooked || (SplitAt.TryGetValue(piece.BodyPointer, out var when) && Time.time - when < 1f);
            var moved = new List<(int X, int Y, int Z)>();
            if (split)
            {
                if (piece.Lost <= 0f)
                    piece.Lost = Time.time;
                moved = FollowCutOff(piece, gone);
                if (moved.Count > 0)
                    gone = gone.Except(moved).ToList();
                if (gone.Count > 0 && Time.time - piece.Lost < WaitForCutOff)
                    return moved.Count > 0;
            }
            piece.Lost = 0f;
            if (gone.Count == 0)
                return true;
            foreach (var cell in gone)
                Remove(piece, cell);
            var rim = RimOf(piece, gone);
            foreach (float chance in new[] { 0.55f, 0.25f })
            {
                var frayed = rim.Where(_ => Random.value < chance).ToList();
                foreach (var cell in frayed)
                    Remove(piece, cell);
                rim = RimOf(piece, frayed.Count > 0 ? frayed : gone);
            }
            foreach (var (x, y, z) in RimOf(piece, gone).Concat(rim))
            {
                float roll = Random.value;
                if (roll < 0.45f)
                    piece.V.Paint(x, x, y, y, z, z, Outfits.Blood);
                else if (roll < 0.7f)
                    piece.V.Paint(x, x, y, y, z, z, Outfits.DarkBlood);
            }
            return true;
        }

        // Cloth whose body went off in a piece of its own (cut off by the cutter, or broken away) moves onto that
        // piece. It's found by looking for a voxel mesh nearby, other than the body's own, that now has voxels where
        // the cloth's body cells were. Returns the cloth voxels that moved.
        private static List<(int X, int Y, int Z)> FollowCutOff(Piece piece, List<(int X, int Y, int Z)> gone)
        {
            var moved = new List<(int X, int Y, int Z)>();
            var centre = piece.Object.transform.position;
            var ownMeshes = new HashSet<IntPtr>(piece.Known ?? new HashSet<IntPtr>()) { piece.Body.Pointer };
            if (_meshesFrame != Time.frameCount)
            {
                _meshesFrame = Time.frameCount;
                _meshes = GameServices.FindObjects<VoxelMesh>();
            }
            foreach (var mesh in _meshes)
            {
                if (mesh == null || ownMeshes.Contains(mesh.Pointer) || !mesh.gameObject.activeInHierarchy
                    || Vector3.Distance(mesh.transform.position, centre) > 1.5f)
                    continue;
                if (!ReadVoxels(mesh, out var size, out var enabled))
                    continue;
                // From the piece's cloth cells to the mesh's voxels, worked out once rather than per cell.
                var grid = mesh.transform;
                var space = piece.Object.transform;
                var origin = grid.InverseTransformPoint(space.position);
                var ex = grid.InverseTransformVector(space.TransformVector(Vector3.right * Voxels.Size));
                var ey = grid.InverseTransformVector(space.TransformVector(Vector3.up * Voxels.Size));
                var ez = grid.InverseTransformVector(space.TransformVector(Vector3.forward * Voxels.Size));
                var taken = new List<(int X, int Y, int Z)>();
                foreach (var cell in gone)
                {
                    if (moved.Contains(cell) || !piece.Anchors.TryGetValue(cell, out var under))
                        continue;
                    var local = origin + ex * under.Item1 + ey * under.Item2 + ez * under.Item3;
                    int x = Mathf.FloorToInt(local.x), y = Mathf.FloorToInt(local.y), z = Mathf.FloorToInt(local.z);
                    if (x >= 0 && y >= 0 && z >= 0 && x < size.x && y < size.y && z < size.z && enabled[(z * size.y + y) * size.x + x])
                        taken.Add(cell);
                }
                if (taken.Count < 3)
                    continue;
                Split(piece, mesh, taken);
                moved.AddRange(taken);
            }
            return moved;
        }

        // A new piece of clothing on a cut-off piece, made of the cloth voxels that went with it.
        private static void Split(Piece piece, VoxelMesh onto, List<(int X, int Y, int Z)> cells)
        {
            var voxels = new Voxels();
            foreach (var colour in piece.Colours)
                voxels.Key(colour.Key, colour.Value);
            var split = new Piece
            {
                Limb = Creatures.LimbFromGameObject(onto.gameObject),
                Body = onto,
                BodyPointer = onto.Pointer,
                Colours = piece.Colours,
                Known = new HashSet<IntPtr>(piece.Known ?? new HashSet<IntPtr>()) { onto.Pointer },
                V = voxels,
                Fit = piece.Fit,
                Min = piece.Min,
                Cell = piece.Cell,
                Name = piece.Name,
                CutOff = true,
            };
            foreach (var cell in cells)
            {
                voxels.Set(cell.X, cell.Y, cell.Z, piece.V.At(cell.X, cell.Y, cell.Z));
                split.Anchors[cell] = piece.Anchors[cell];
                Remove(piece, cell);
            }
            var go = new GameObject(piece.Name);
            go.transform.SetPositionAndRotation(piece.Object.transform.position, piece.Object.transform.rotation);
            go.transform.localScale = piece.Object.transform.lossyScale;
            go.transform.SetParent(onto.transform, true);
            split.Object = go;
            split.On = onto.transform;
            split.Wholeness = split.Limb != null ? split.Limb.GetWholeness() : 0f;
            split.NextLook = Time.time + 1f;
            voxels.HideWhere(BodyCellsOf(split, onto).Has);
            split.Filter = go.AddComponent<MeshFilter>();
            split.Filter.sharedMesh = voxels.BuildMesh(piece.Name);
            go.AddComponent<MeshRenderer>().sharedMaterial = piece.Object.GetComponent<MeshRenderer>().sharedMaterial;
            Pieces.Add(split);
            CutOff++;
        }

        private static void Remove(Piece piece, (int X, int Y, int Z) cell)
        {
            piece.V.Clear(cell.X, cell.Y, cell.Z);
            piece.Anchors.Remove(cell);
        }

        // The cloth still there round some removed voxels.
        private static List<(int X, int Y, int Z)> RimOf(Piece piece, List<(int X, int Y, int Z)> removed)
        {
            var rim = new HashSet<(int, int, int)>();
            foreach (var (x, y, z) in removed)
            {
                foreach (var next in Around(x, y, z))
                {
                    if (piece.V.Has(next.Item1, next.Item2, next.Item3))
                        rim.Add(next);
                }
            }
            return rim.ToList();
        }

        private static IEnumerable<(int, int, int)> Around(int x, int y, int z)
        {
            yield return (x + 1, y, z);
            yield return (x - 1, y, z);
            yield return (x, y + 1, z);
            yield return (x, y - 1, z);
            yield return (x, y, z + 1);
            yield return (x, y, z - 1);
        }

        private static void Rebuild(Piece piece)
        {
            var old = piece.Filter.sharedMesh;
            if (piece.V.Count == 0)
            {
                Object.Destroy(piece.Object);
                if (old != null)
                    Object.Destroy(old);
                return;
            }
            piece.Filter.sharedMesh = piece.V.BuildMesh(piece.Name, old);
        }

        // A hat knocked off a badly hurt head: it falls and lies where it lands, for a while.
        private static void Drop(Piece piece)
        {
            var go = piece.Object;
            go.transform.SetParent(null, true);
            var bounds = piece.Filter.sharedMesh.bounds;
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.3f;
            body.AddForce(Vector3.up * 1.5f + Random.insideUnitSphere, ForceMode.Impulse);
            Object.Destroy(go, 90f);
        }

        // ------------------------------------------------------------ the body under the clothes

        // Where a world point is in a piece's cloth grid: cloth voxel (x, y, z) is centred on (x, y, z).
        private static Vector3 CellAt(Piece piece, Vector3 world) => piece.Object.transform.InverseTransformPoint(world) / Voxels.Size;

        private static Vector3 WorldOf(Piece piece, (int X, int Y, int Z) cell)
            => piece.Object.transform.TransformPoint(new Vector3(cell.X, cell.Y, cell.Z) * Voxels.Size);

        // A body's voxels that are still there, as cells of the piece's grid. Destroyed voxels are switched off in the
        // game's voxel grid; its mesh can keep their faces for a while, so the grid is what counts.
        private static BodyGrid BodyCellsOf(Piece piece, VoxelMesh mesh)
        {
            var fit = piece.Fit;
            var result = new BodyGrid(fit);
            if (mesh == null || !ReadVoxels(mesh, out var size, out var enabled))
                return result;
            // From the grid's voxels to the piece's: the mesh object sits at the grid's corner, a voxel to a unit.
            var grid = mesh.transform;
            var space = piece.Object.transform;
            var origin = space.InverseTransformPoint(grid.position) / Voxels.Size + Vector3.one * 0.5f;
            var ex = space.InverseTransformVector(grid.TransformVector(Vector3.right)) / Voxels.Size;
            var ey = space.InverseTransformVector(grid.TransformVector(Vector3.up)) / Voxels.Size;
            var ez = space.InverseTransformVector(grid.TransformVector(Vector3.forward)) / Voxels.Size;
            int i = 0;
            for (int z = 0; z < size.z; z++)
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++, i++)
            {
                if (!enabled[i])
                    continue;
                var a = origin + ex * x + ey * y + ez * z;
                var b = a + ex + ey + ez;
                var lo = Vector3.Min(a, b);
                var hi = Vector3.Max(a, b);
                if (hi.x < -0.5f || hi.y < -0.5f || hi.z < -0.5f || lo.x > fit.X + 0.5f || lo.y > fit.Y + 0.5f || lo.z > fit.Z + 0.5f)
                    continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(lo.x + 0.05f), 0, fit.X - 1), x1 = Mathf.Clamp(Mathf.FloorToInt(hi.x - 0.05f), 0, fit.X - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(lo.y + 0.05f), 0, fit.Y - 1), y1 = Mathf.Clamp(Mathf.FloorToInt(hi.y - 0.05f), 0, fit.Y - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt(lo.z + 0.05f), 0, fit.Z - 1), z1 = Mathf.Clamp(Mathf.FloorToInt(hi.z - 0.05f), 0, fit.Z - 1);
                for (int cx = x0; cx <= x1; cx++)
                for (int cy = y0; cy <= y1; cy++)
                for (int cz = z0; cz <= z1; cz++)
                    result.Add(cx, cy, cz);
            }
            return result;
        }

        // Reading the voxel grid one voxel at a time goes through the game for each one. Its voxels sit in one block
        // of memory, so they're read from there instead, after checking once that both ways agree. The flags come back
        // x fastest, then y, then z.
        private static bool[] _enabled = new bool[0];
        private static bool? _rawWorks;

        private static bool ReadVoxels(VoxelMesh mesh, out Vector3Int size, out bool[] enabled)
        {
            enabled = null;
            size = Vector3Int.zero;
            var data = mesh?.Data;
            if (data == null)
                return false;
            var grid = data.Size;
            size = new Vector3Int(grid.x, grid.y, grid.z);
            int total = size.x * size.y * size.z;
            if (total <= 0)
                return false;
            if (_enabled.Length < total)
                _enabled = new bool[total];
            enabled = _enabled;
            if (_rawWorks != false && ReadRaw(data, size, enabled))
                return true;
            int i = 0;
            for (int z = 0; z < size.z; z++)
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++, i++)
                enabled[i] = data[new Vector3Int(x, y, z)].enabled;
            return true;
        }

        private static bool ReadRaw(VoxelMesh.Voxels data, Vector3Int size, bool[] enabled)
        {
            try
            {
                var array = data.m_voxels;
                var buffer = (VoxelMesh.Voxel*)array.m_Buffer;
                int length = array.m_Length;
                if (buffer == null || length < size.x * size.y * size.z)
                    return false;
                int origin = data.GetIntIndex(0, 0, 0);
                int sx = data.GetIntIndex(Math.Min(1, size.x - 1), 0, 0) - origin;
                int sy = data.GetIntIndex(0, Math.Min(1, size.y - 1), 0) - origin;
                int sz = data.GetIntIndex(0, 0, Math.Min(1, size.z - 1)) - origin;
                int i = 0;
                for (int z = 0; z < size.z; z++)
                for (int y = 0; y < size.y; y++)
                {
                    int row = origin + y * sy + z * sz;
                    for (int x = 0; x < size.x; x++, i++)
                    {
                        int at = row + x * sx;
                        enabled[i] = at >= 0 && at < length && buffer[at].enabled;
                    }
                }
                if (_rawWorks == null)
                {
                    // The first time, a spread of voxels is read both ways and they have to agree.
                    bool agree = true;
                    int stride = Math.Max(1, size.x * size.y * size.z / 97);
                    for (int n = 0, j = 0; n < 97 && agree; n++, j = (j + stride) % (size.x * size.y * size.z))
                    {
                        int x = j % size.x, y = j / size.x % size.y, z = j / (size.x * size.y);
                        agree = enabled[j] == data[new Vector3Int(x, y, z)].enabled;
                    }
                    _rawWorks = agree;
                    if (!agree)
                        FruktLog.Debug("Reading voxels from memory didn't match the game's; reading them one at a time instead.");
                    return agree;
                }
                return true;
            }
            catch (Exception e)
            {
                _rawWorks = false;
                FruktLog.Debug("Reading voxels from memory failed: " + e.Message);
                return false;
            }
        }

        private static Vector3Int GridOf(VoxelMesh mesh)
        {
            var size = mesh?.Data?.Size;
            return size.HasValue ? new Vector3Int(size.Value.x, size.Value.y, size.Value.z) : Vector3Int.zero;
        }

        // The body part's size, in the space of the bone that carries it: its voxel grid's corners.
        private static bool BodyBounds(AbstractLimb limb, Transform on, out Vector3 min, out Vector3 max)
        {
            min = max = Vector3.zero;
            var mesh = limb.GetVoxelMesh();
            var data = mesh?.Data;
            if (data == null)
                return false;
            var size = data.Size;
            var grid = mesh.transform;
            var a = on.InverseTransformPoint(grid.position);
            var b = on.InverseTransformPoint(grid.TransformPoint(new Vector3(size.x, size.y, size.z)));
            min = Vector3.Min(a, b);
            max = Vector3.Max(a, b);
            return size.x > 0 && size.y > 0 && size.z > 0;
        }
    }
}
