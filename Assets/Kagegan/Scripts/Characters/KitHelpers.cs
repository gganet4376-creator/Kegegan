using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>Shared building blocks for scripted techniques: cinematic damage, line sweeps, blade moves, afterimages.</summary>
    public static class KitHelpers
    {
        /// <summary>Damage inside a cinematic (the target ignores normal hits there). Never kills: the closing real hit does.</summary>
        public static void DealCinematic(Fighter a, Fighter t, float dmg, Color c, bool heavy = true)
        {
            if (t == null || !t.Alive) return;
            if (a != null) dmg *= a.DamageMul * (a.Kit != null ? a.Kit.OutgoingMul(null) : 1f);
            t.Hp = Mathf.Max(1f, t.Hp - dmg);
            t.Stats.damageTaken += dmg;
            if (a != null) a.Stats.damageDealt += dmg;
            t.Anim.Flinch(1f, false);
            t.Flash(Color.white);
            Fx.Hit(t.Center, c, heavy);
            Sfx.Play(heavy ? SfxId.HeavyHit : SfxId.Hit, 0.9f, Random.Range(0.92f, 1.08f));
            CameraRig.Shake(heavy ? 0.3f : 0.15f);
            if (a != null && a.IsPlayer) { if (heavy) Haptics.Medium(); else Haptics.Light(); }
        }

        /// <summary>Hits every enemy within <paramref name="width"/> of the segment once (tracked in <paramref name="already"/>).</summary>
        public static int LineHit(Fighter a, Vector3 from, Vector3 to, float width, HitInfo template, List<Fighter> already)
        {
            int n = 0;
            Vector3 seg = KMath.Flat(to - from);
            float len = seg.magnitude;
            Vector3 dirSeg = len > 0.01f ? seg / len : a.transform.forward;
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var f = Fighter.All[i];
                if (!f.Alive || f.Team == a.Team || f.Team == Team.Neutral || (already != null && already.Contains(f))) continue;
                Vector3 rel = KMath.Flat(f.transform.position - from);
                float along = Mathf.Clamp(Vector3.Dot(rel, dirSeg), 0f, len);
                if ((rel - dirSeg * along).magnitude > width + f.BodyRadius) continue;
                if (Mathf.Abs(f.transform.position.y - Mathf.Lerp(from.y, to.y, len > 0f ? along / len : 0f)) > 2.2f) continue;
                var h = template;
                h.Attacker = a;
                h.Damage = template.Damage * a.DamageMul * (a.Kit != null ? a.Kit.OutgoingMul(h.Move) : 1f);
                h.Point = f.Center;
                if (h.Dir.sqrMagnitude < 0.01f) h.Dir = dirSeg;
                already?.Add(f);
                var r = f.ReceiveHit(ref h);
                if (r != HitResult.Miss && r != HitResult.PerfectDodge) n++;
            }
            return n;
        }

        public static HitInfo Hit(float dmg, float knock, float launch, HitKind kind, Color c, bool knockdown = false, bool unblockable = false)
        {
            return new HitInfo
            {
                Damage = dmg, Stamina = 25f, Knockback = knock, Launch = launch, Hitstun = 0.6f, Kind = kind, Knockdown = knockdown,
                Unblockable = unblockable, Parryable = !unblockable, FxColor = c, Shake = 0.3f, HitStop = 0.07f, Sfx = SfxId.HeavyHit
            };
        }

        /// <summary>Single direct hit on one target (used by scripted dashes and teleport strikes).</summary>
        public static HitResult Strike(Fighter a, Fighter t, HitInfo h)
        {
            if (t == null || !t.Alive) return HitResult.Miss;
            h.Attacker = a;
            h.Damage *= a.DamageMul * (a.Kit != null ? a.Kit.OutgoingMul(h.Move) : 1f);
            h.Point = t.Center;
            if (h.Dir.sqrMagnitude < 0.01f) h.Dir = KMath.Flat(t.transform.position - a.transform.position).normalized;
            return t.ReceiveHit(ref h);
        }

        /// <summary>A blade swing driven by a captured sword clip, with a coloured arc at contact.</summary>
        public static Move Blade(string name, string clip, float contact, float dmg, float startup, float recovery, Color c, Vector3 slashEuler, float reach = 0.55f)
        {
            return new Move
            {
                Name = name, Windup = PoseLib.HookRWind, Strike = PoseLib.HookR, Startup = startup, Active = 0.1f, Recovery = recovery,
                Damage = dmg, StaminaDamage = 12f, Knockback = 3f, Hitstun = 0.45f, Kind = HitKind.Light, Bone = HitBone.HandR,
                Radius = 0.7f, Reach = new Vector3(0, 0, reach), Lunge = 3.5f, StaminaCost = 4f, Mocap = clip, MocapContact = contact,
                FxColor = c, SlashFx = true, SlashEuler = slashEuler, Sfx = SfxId.Whoosh, HitSfx = SfxId.Hit, Shake = 0.12f, HitStop = 0.06f, Range = 2.4f
            };
        }

        /// <summary>Shadow afterimage along the body (cheap: a handful of glyph particles).</summary>
        public static void Afterimage(Fighter f, Color c, int count = 4)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 p = f.transform.position + Vector3.up * Random.Range(0.2f, f.Height) + Random.insideUnitSphere * 0.2f;
                Fx.Glyph(p, -f.Velocity * 0.05f + Vector3.up * 0.2f, c, Random.Range(0.25f, 0.45f), 0.35f);
            }
            Fx.Trail(f.Center, c, 0.6f);
        }

        /// <summary>Silhouette left behind by a substitution / dodge.</summary>
        public static void Silhouette(Fighter f, Color c)
        {
            Vector3 p = f.transform.position;
            for (int i = 0; i < 6; i++) Fx.Glow(p + Vector3.up * (0.25f + i * f.Height * 0.16f), c, 0.55f, 0.45f);
            Fx.Smoke(f.Center, 0.7f, new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.6f, 0.7f), 8);
        }

        /// <summary>Point behind the target (relative to the attacker), clamped to the arena.</summary>
        public static Vector3 Behind(Fighter a, Fighter t, float dist = 1.1f)
        {
            Vector3 d = KMath.Flat(t.transform.position - a.transform.position);
            if (d.sqrMagnitude < 0.01f) d = t.transform.forward * -1f;
            return Arena.ClampInside(t.transform.position + d.normalized * dist, 1f);
        }

        // ---------- boss tools ----------
        static readonly Dictionary<Fighter, List<Renderer>> hidden = new Dictionary<Fighter, List<Renderer>>();

        /// <summary>Hides or shows every renderer of a fighter (eclipse, shed skin). Only re-enables what it disabled.</summary>
        public static void SetHidden(Fighter f, bool hide)
        {
            if (f == null) return;
            if (hide)
            {
                if (hidden.ContainsKey(f)) return;
                var list = new List<Renderer>();
                foreach (var r in f.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; list.Add(r); }
                hidden[f] = list;
            }
            else if (hidden.TryGetValue(f, out var list))
            {
                foreach (var r in list) if (r != null) r.enabled = true;
                hidden.Remove(f);
            }
        }

        /// <summary>Readable warning before a heavy attack: body flash, a shrinking ground ring and a rising charge sound.</summary>
        public static void Telegraph(Fighter f, Color c, float secs, float radius = 2.2f)
        {
            if (f == null) return;
            f.Flash(c, 0.9f);
            Fx.Ring(f.transform.position + Vector3.up * 0.06f, c, radius, 0.4f, secs, Quaternion.identity);
            Fx.Glow(f.Center + Vector3.up * 0.5f, c, 1.1f, secs * 0.8f);
            Sfx.Play(SfxId.Charge, 0.8f, 1.6f - Mathf.Clamp01(secs) * 0.6f);
        }

        /// <summary>Warning marker on the ground at a point (not on a fighter).</summary>
        public static void Mark(Vector3 p, Color c, float radius, float secs)
        {
            p.y = 0.06f;
            Fx.Ring(p, c, radius * 1.4f, radius, secs, Quaternion.identity);
            Fx.Glow(p + Vector3.up * 0.3f, c, radius * 0.6f, secs);
        }

        /// <summary>Wraps a move so it always shows a warning before it strikes and gets a slightly longer windup.</summary>
        public static Move Warned(Move m, Color c, float extra = 0.12f)
        {
            m.Startup += extra;
            var prev = m.OnStart;
            float secs = m.Startup;
            m.OnStart = f => { prev?.Invoke(f); Telegraph(f, c, secs, 1.6f); };
            return m;
        }

        /// <summary>A copy of a boss that dies from any hit (shed skin, illusions). With a brain it fights for a few seconds.</summary>
        public static Fighter Decoy(Fighter owner, Vector3 pos, StanceKind stance, RunStyle run, bool fights, float life, Color tint)
        {
            pos = Arena.ClampInside(pos, 1f);
            var look = owner.Rig.Look;
            float scale = owner.Rig.transform.localScale.x;
            var f = FighterFactory.Make(owner.DisplayName, look, stance, run, new MinionKit(MinionType.SnakeAdept), owner.Team, pos, Roster.FighterRoot, 1f, owner.MoveSpeed, scale);
            f.IsClone = true;
            f.Owner = owner;
            f.DamageMul = owner.DamageMul * 0.35f;
            f.InfiniteChakra = false;
            f.transform.rotation = owner.transform.rotation;
            f.Rig.SetEmit(0.25f, tint);
            if (fights)
            {
                var tune = DifficultyTuning.For(Difficulty.Warrior);
                tune.aggression = 1f; tune.reaction = 0.25f; tune.blockChance = 0f;
                var brain = f.gameObject.AddComponent<AIBrain>();
                brain.Setup(f, Archetype.Clone, tune);
                f.Target = owner.Target;
            }
            var l = f.gameObject.AddComponent<CloneLife>();
            l.Lifetime = life;
            f.OnDied += (dead, killer) => l.Poof();
            return f;
        }
    }
}
