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

        protected override void Update()
        {
            if (F == null || !F.Alive) return;
            if (F.Target == null || !F.Target.Alive || F.Target.Team == F.Team) F.Target = Combat.Nearest(F.transform.position, F.Team);
            base.Update();
            if (!Active || TimeFx.Paused) return;
            UseSkills();
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
