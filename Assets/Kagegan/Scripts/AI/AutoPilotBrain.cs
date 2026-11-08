using System.Collections;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Drives the player's fighter while the "Авто" button is active. Fights like a Legend enemy, but dodges every
    /// incoming melee hit inside the perfect-dodge window (the hit lands within 0.2 s of the dodge start), punishes
    /// with the counter afterwards and spends techniques and the ultimate.
    /// </summary>
    public class AutoPilotBrain : AIBrain
    {
        float techTimer = 1.5f;
        float lastDodge = -9f;
        Fighter dodgedFrom;

        public void Init(Fighter f)
        {
            var tune = DifficultyTuning.For(Difficulty.Legend);
            tune.reaction = 0.06f;
            tune.blockChance = 0.85f;
            tune.parryChance = 0.45f;
            tune.aggression = 1f;
            Setup(f, Archetype.Striker, tune);
            UsesTokens = false;
            f.Brain = null; // still the human player for kits, finishers and HUD
        }

        float mashT, downSince = -1f, mountT, mountTry, retreatCd;

        protected override void Update()
        {
            if (F == null || !F.Alive) return;
            if (F.Target == null || !F.Target.Alive || F.Target.Team == F.Team) PickTarget();
            if (Active && !TimeFx.Paused && Situational(Time.deltaTime)) { F.SetMove(Vector3.zero); F.SetBlock(false); return; }
            base.Update();
            if (!Active || TimeFx.Paused) return;
            UseSkills();
        }

        /// <summary>Lock onto whoever is about to hit us, else a finishable / nearby weak enemy, else keep the current one.</summary>
        protected override void PickTarget()
        {
            var threat = F.IncomingAttacker();
            var cur = F.Target;
            bool curOk = cur != null && cur.Alive && cur.Team != F.Team && cur.Team != Team.Neutral;
            if (threat != null && !(curOk && cur.Target == F && (cur.InStartup || cur.InActiveFrames))) { F.Target = threat; return; }
            Fighter best = null;
            float bs = float.MaxValue;
            var all = Fighter.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || !e.Alive || e.Team == F.Team || e.Team == Team.Neutral) continue;
                float d = KMath.Flat(e.transform.position - F.transform.position).magnitude;
                // distance first, then prefer wounded / downed enemies and keep the current one (hysteresis)
                float score = d + e.HpFrac * 2.5f - (F.Kit.CanFinish(e) ? 4f : 0f) - (e.State == FState.Knockdown ? 1.5f : 0f) - (e == cur ? 2f : 0f);
                if (score < bs) { bs = score; best = e; }
            }
            if (best != null) F.Target = best;
        }

        /// <summary>Context actions the generic AI does not know: struggle, trip, mount, finishers, retreat when swarmed.</summary>
        bool Situational(float dt)
        {
            // break holds / mounts by mashing
            if (F.CanBreakFree)
            {
                mashT -= dt;
                if (mashT <= 0f) { F.Press(Cmd.Dodge); mashT = Random.Range(0.08f, 0.12f); }
                return true;
            }
            // on the ground: sweep a careless enemy after a short human-like delay
            if (F.State == FState.Knockdown)
            {
                if (downSince < 0f) downSince = Time.time;
                if (Time.time - downSince > 0.25f && F.CanTrip) F.TryTrip();
                return true;
            }
            downSince = -1f;
            // sitting on a downed enemy: rain blows (the last one becomes the hammer)
            if (F.State == FState.Mount)
            {
                mountT -= dt;
                if (mountT <= 0f) { F.Press(Cmd.Light); mountT = Random.Range(0.27f, 0.34f); }
                return true;
            }
            var t = F.Target;
            if (t == null || !F.CanAct) return false;
            float d = KMath.Flat(t.transform.position - F.transform.position).magnitude;
            if (F.Kit.CanFinish(t) && d < 3f) { F.Press(Cmd.Heavy); return true; }
            if (F.CanMountTarget && Time.time >= mountTry)
            {
                mountTry = Time.time + 1.5f;
                if (Random.value < 0.7f) { F.Press(Cmd.Grab); return true; }
            }
            // badly hurt and surrounded: back off, the skills timer will throw ranged techniques
            retreatCd -= dt;
            if (F.HpFrac < 0.3f && retreatCd <= 0f)
            {
                int near = 0;
                for (int i = 0; i < Fighter.All.Count; i++)
                {
                    var e = Fighter.All[i];
                    if (e != null && e.Alive && e.Team != F.Team && e.Team != Team.Neutral && KMath.Flat(e.transform.position - F.transform.position).sqrMagnitude < 9f) near++;
                }
                retreatCd = 1f;
                if (near >= 2) { retreatUntil = Time.time + Random.Range(0.6f, 1f); retreatCd = 4f; techTimer = Mathf.Min(techTimer, 0.3f); }
            }
            return false;
        }

        protected override void React(float dt)
        {
            if (PerfectDodge()) return;
            base.React(dt);
        }

        /// <summary>Dodge a hit that is about to land so it arrives inside the perfect-dodge window.</summary>
        bool PerfectDodge()
        {
            if (!F.CanAct || F.Stamina < 14f || Time.time - lastDodge < 0.4f) return false;
            var all = Fighter.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || !e.Alive || e.Team == F.Team || !e.InStartup) continue;
                var m = e.CurMove;
                if (m == null || m.Damage <= 0f || (e == dodgedFrom && e.MoveTime < 0.05f)) continue;
                float d = KMath.Flat(e.transform.position - F.transform.position).magnitude;
                if (d > m.Range + 1.3f) continue;
                float left = m.Startup - e.MoveTime;
                // press a little before the active frames: the hit then lands ~0.05-0.15 s into the dodge
                if (left > 0.13f) continue;
                Vector3 away = KMath.Flat(F.transform.position - e.transform.position).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? 1f : -1f);
                moveDir = (away * 0.4f + side).normalized;
                F.SetMove(moveDir);
                F.Press(Cmd.Dodge);
                lastDodge = Time.time;
                dodgedFrom = e;
                return true;
            }
            return false;
        }

        void UseSkills()
        {
            techTimer -= Time.deltaTime;
            if (techTimer > 0f || F.Target == null || !F.CanAct) return;
            techTimer = Random.Range(1.2f, 2.5f);
            float d = KMath.Flat(F.Target.transform.position - F.transform.position).magnitude;
            if (d > 9f) return;
            if (F.Rage >= 100f && d < 5f) { F.Press(Cmd.Ult); return; }
            var techs = F.Kit.Techs;
            int start = Random.Range(0, techs.Length);
            for (int k = 0; k < techs.Length; k++)
            {
                int i = (start + k) % techs.Length;
                var t = techs[i];
                if (t == null || !t.Ready(F) || (t.Offensive && d > t.Range)) continue;
                if (t.Hold) StartCoroutine(HoldTech(i));
                else F.Press(Cmd.Tech, i);
                return;
            }
        }

        IEnumerator HoldTech(int i)
        {
            F.SetTechHeld(i, true);
            yield return new WaitForSeconds(Random.Range(0.6f, 1.4f));
            if (F != null) F.SetTechHeld(i, false);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopAllCoroutines();
            if (F == null || F.Kit == null) return;
            for (int i = 0; i < F.Kit.Techs.Length; i++) F.SetTechHeld(i, false);
            F.SetMove(Vector3.zero);
            F.SetBlock(false);
        }
    }
}
