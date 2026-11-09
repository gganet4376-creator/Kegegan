using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Brain for regular enemies. Built on <see cref="AIBrain"/>, but each minion gets its own reaction speed, error rate
    /// and temper, and each design type plays its role: the assassin circles behind, the mercenary keeps distance and rolls
    /// away, the bomber seeds mines and pushes the fight onto them, the bandit grabs the player for the group, the cyber
    /// soldier stops when overheated, the bone warrior fights harder with its spear than without.
    /// It never reads player input: it reacts only to what is visible on screen (wind-ups, positions, projectiles).
    /// </summary>
    public class MinionBrain : AIBrain
    {
        MinionKit kit;
        MinionType mtype;
        float mistakeChance;      // chance to miss a reaction entirely
        float distractedUntil;    // during this time no defensive reactions at all
        float nextMistakeRoll;
        float rollCooldown;
        float flankSide;

        public void Init(MinionType t)
        {
            mtype = t;
            kit = F.Kit as MinionKit;
            // personal temperament: Tune is a struct, so this only changes this minion
            Tune.reaction *= Random.Range(0.8f, 1.4f);
            Tune.aggression = Mathf.Clamp01(Tune.aggression + Random.Range(-0.12f, 0.12f));
            mistakeChance = Random.Range(0.08f, 0.22f) * (1.4f - Tune.blockChance);
            flankSide = Random.value < 0.5f ? -1f : 1f;
            switch (t)
            {
                case MinionType.ForestAssassin: Tune.reaction *= 0.85f; PreferredRange = 1.2f; break;
                case MinionType.CityMerc: Type = Archetype.Ranged; PreferredRange = 6.5f; break;
                case MinionType.CityBomber: Type = Archetype.Ranged; PreferredRange = 8f; break;
                case MinionType.CityCyber: Tune.reaction *= 0.9f; PreferredRange = 1.4f; break;
                case MinionType.ForestBandit: PreferredRange = 1.3f; break;
                case MinionType.MoonBone: Tune.reaction *= 1.2f; PreferredRange = 1.8f; break; // slow and heavy
            }
        }

        protected override void React(float dt)
        {
            if (Time.time >= nextMistakeRoll)
            {
                // sometimes a minion just looks the wrong way for a moment
                nextMistakeRoll = Time.time + Random.Range(1.5f, 3f);
                if (Random.value < mistakeChance) distractedUntil = Time.time + Random.Range(0.4f, 0.9f);
            }
            if (Time.time < distractedUntil) return;
            if (kit != null && kit.Overheated) return; // venting: no defence at all
            base.React(dt);
        }

        protected override void Think()
        {
            var t = F.Target;
            if (t == null || kit == null || !F.CanAct || F.State == FState.Hitstun || F.State == FState.Juggle) { base.Think(); return; }
            float d = DistToTarget;

            // squad: a held target is a free hit for everyone nearby
            if (Squad.IsPinned(t) && Squad.PinnedBy != F)
            {
                moveDir = DirToTarget;
                if (d < PreferredRange + 0.6f && Time.time >= nextAttackTime)
                {
                    F.Press(Random.value < 0.5f ? Cmd.Heavy : Cmd.Light);
                    nextAttackTime = Time.time + 0.25f;
                }
                else if (d > 4f && Random.value < 0.3f) TryTechnique(d);
                return;
            }

            switch (mtype)
            {
                case MinionType.ForestAssassin: if (AssassinThink(t, d)) return; break;
                case MinionType.CityMerc: if (MercThink(t, d)) return; break;
                case MinionType.CityBomber: if (BomberThink(t, d)) return; break;
                case MinionType.ForestBandit: if (BanditThink(t, d)) return; break;
                case MinionType.CityCyber: if (CyberThink(t, d)) return; break;
                case MinionType.MoonBone: if (BoneThink(t, d)) return; break;
            }
            base.Think();
        }

        bool Use(int i, float d)
        {
            var techs = F.Kit.Techs;
            if (i < 0 || i >= techs.Length || techs[i] == null || !techs[i].Ready(F)) return false;
            if (techs[i].Offensive && d > techs[i].Range) return false;
            F.Press(Cmd.Tech, i);
            return true;
        }

        bool TargetBusy(Fighter t) => t.State == FState.Attack || t.State == FState.Block || t.State == FState.Stunned || t.State == FState.Hitstun;

        // Убийца: circles to the target's back while allies keep them busy, then strikes from behind
        bool AssassinThink(Fighter t, float d)
        {
            bool alliesEngaged = Combat.AttackTokensInUse > (hasToken ? 1 : 0);
            Vector3 back = t.transform.position - t.transform.forward * 1.3f + Vector3.Cross(Vector3.up, t.transform.forward) * flankSide * 0.6f;
            float behindDot = Vector3.Dot(t.transform.forward, KMath.Flat(F.transform.position - t.transform.position).normalized);
            if (alliesEngaged && behindDot > -0.5f && d < 7f)
            {
                // not behind yet: swing around wide, out of reach
                Vector3 to = KMath.Flat(Arena.ClampInside(back, 1f) - F.transform.position);
                Vector3 side = Vector3.Cross(Vector3.up, DirToTarget) * flankSide;
                moveDir = (to.normalized + (d < 2.5f ? -DirToTarget + side : side) * 0.6f).normalized;
                if (Time.time >= nextAttackTime && Random.value < 0.25f && Use(0, d)) { Pause(); return true; }
                return true;
            }
            if (Time.time < nextAttackTime) return false;
            if (behindDot < -0.5f && d < 2f) { F.Press(Cmd.Heavy); Pause(); return true; }
            if (d > 5f && Random.value < 0.35f && (Use(0, d) || Use(1, d))) { Pause(); return true; }
            // wait in counter stance when the target is about to swing
            if (d < 2.4f && t.InStartup && Random.value < 0.4f && Use(3, d)) { Pause(); return true; }
            if (F.HpFrac < 0.4f && d < 2f && Random.value < 0.3f && Use(2, d)) { Pause(); return true; }
            return false;
        }

        // Наёмник: keeps the gap, rolls out of pressure, fires bursts, shields when hurt, butt-strikes onto mines
        bool MercThink(Fighter t, float d)
        {
            rollCooldown -= 0.17f;
            if (d < 2.6f)
            {
                var mine = MineTrap.Near(t.transform.position, 4f);
                bool pushOnto = mine != null && Vector3.Dot(KMath.Flat(mine.transform.position - t.transform.position).normalized, DirToTarget) > 0.5f;
                if ((pushOnto || Random.value < 0.35f) && Time.time >= nextAttackTime && Use(2, d)) { Pause(); return true; }
                if (rollCooldown <= 0f && Random.value < 0.5f)
                {
                    moveDir = -DirToTarget;
                    F.Press(Cmd.Dodge);
                    rollCooldown = Random.Range(2.5f, 4f);
                    return true;
                }
                moveDir = -DirToTarget;
                return true;
            }
            if (F.HpFrac < 0.6f && !kit.ShieldUp && Random.value < 0.15f && Use(1, d)) return true;
            Vector3 side = Vector3.Cross(Vector3.up, DirToTarget) * strafeSign;
            moveDir = d > 9f ? DirToTarget : d < 5f ? -DirToTarget + side * 0.4f : side * 0.6f;
            if (Random.value < 0.03f) strafeSign = -strafeSign;
            if (Time.time >= nextAttackTime && d < 14f && Use(0, d)) nextAttackTime = Time.time + Random.Range(1.5f, 2.8f) * Mathf.Lerp(1.4f, 0.7f, Tune.aggression);
            return true;
        }

        // Подрывник: lays mines between itself and the target, lobs bombs and rockets, blast-jumps out when cornered
        bool BomberThink(Fighter t, float d)
        {
            if (d < 2.4f && Use(3, d)) return true;
            Vector3 side = Vector3.Cross(Vector3.up, DirToTarget) * strafeSign;
            moveDir = d > 11f ? DirToTarget : d < 6f ? -DirToTarget + side * 0.3f : side * 0.5f;
            if (Random.value < 0.03f) strafeSign = -strafeSign;
            if (Time.time < nextAttackTime) return true;
            bool ok = false;
            if (MineTrap.CountOf(F) < 2 && Random.value < 0.45f) ok = Use(1, d);
            if (!ok && TargetBusy(t) && Random.value < 0.5f) ok = Use(2, d); // a busy target can't run from the rockets
            if (!ok) ok = Use(0, d) || Use(2, d);
            nextAttackTime = Time.time + (ok ? Random.Range(1.6f, 3f) : 0.6f) * Mathf.Lerp(1.4f, 0.7f, Tune.aggression);
            return true;
        }

        // Разбойник: brawls, grabs the target when allies are close enough to capitalise
        bool BanditThink(Fighter t, float d)
        {
            if (Time.time < nextAttackTime) return false;
            int allies = 0;
            for (int i = 0; i < Fighter.All.Count; i++)
            {
                var x = Fighter.All[i];
                if (x != F && x.Alive && x.Team == F.Team && KMath.Flat(x.transform.position - t.transform.position).magnitude < 5f) allies++;
            }
            if (allies > 0 && d < 2.2f && Squad.CanHold && Random.value < 0.35f && Use(0, d)) { nextAttackTime = Time.time + 1.2f; return true; }
            if (d > 4f && d < 9f && Random.value < 0.15f && Use(1, d)) { Pause(); return true; }
            if (d < 2.4f && t.InStartup && Random.value < 0.25f && Use(2, d)) { Pause(); return true; }
            return false;
        }

        // Киберсолдат: aggressive bursts until the heat runs high, then backs off to cool down
        bool CyberThink(Fighter t, float d)
        {
            if (kit.Heat > 65f && d < 4f)
            {
                moveDir = -DirToTarget + Vector3.Cross(Vector3.up, DirToTarget) * strafeSign * 0.5f;
                return true;
            }
            if (Time.time < nextAttackTime) return false;
            if (d > 4f && d < 9f && Random.value < 0.3f && Use(0, d)) { Pause(); return true; }
            if (d > 6f && Random.value < 0.1f && Use(3, d)) { Pause(); return true; }
            if (d < 2.4f && Random.value < 0.3f && Use(1, d)) { Pause(); return true; }
            if (d < 3f && t.State == FState.Attack && Random.value < 0.2f && Use(2, d)) { Pause(); return true; }
            return false;
        }

        // Костяной воин: pokes with the spear at its long reach, spins when surrounded, throws the spear when the target runs
        bool BoneThink(Fighter t, float d)
        {
            if (Time.time < nextAttackTime) return false;
            if (kit.Unarmed) return false; // just brawls until the spear regrows
            if (d > 2f && d < 3.4f && Random.value < 0.5f && Use(0, d)) { Pause(); return true; }
            if (d < 2.4f && Random.value < 0.25f && Use(1, d)) { Pause(); return true; }
            if (d > 6f && Random.value < 0.12f && Use(2, d)) { Pause(); return true; }
            if (d > 5f && Random.value < 0.08f && Use(3, d)) { Pause(); return true; }
            return false;
        }
    }
}
