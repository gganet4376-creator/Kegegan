using System.Collections;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Multi-phase boss AI. Phase changes trigger a cinematic roar, a banner and arena events; each boss
    /// subclass chooses technique patterns per phase. Bosses ignore attack tokens and break combos more often.
    /// </summary>
    public abstract class BossBrain : AIBrain
    {
        public float[] Thresholds = { 0.6f, 0.3f };
        public string[] PhaseLines = new string[0];
        public int Phase;
        protected float patternReadyAt;
        protected float patternGap = 2.2f;
        protected float speedBase;
        protected bool transitioning;
        protected bool busy;
        float nextBreakAt;
        float ultReadyAt = float.MaxValue;
        protected float ultEvery = 26f;

        public override bool FinalPhase => Phase >= Thresholds.Length;
        public bool Transitioning => transitioning;

        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            // bosses hit hard and have big patterns; they should not also turtle, so they block / parry less than grunts
            tune.blockChance *= 0.55f;
            tune.parryChance *= 0.4f;
            tune.reaction *= 1.25f;
            base.Setup(f, Archetype.Boss, tune);
            UsesTokens = false;
            PreferredRange = 1.5f;
            speedBase = f.MoveSpeed;
            patternReadyAt = Time.time + 2.5f * tune.bossPace;
        }

        protected override void Think()
        {
            if (transitioning || busy) { moveDir = Vector3.zero; return; }
            int p = 0;
            while (p < Thresholds.Length && F.HpFrac <= Thresholds[p]) p++;
            if (p > Phase && F.Alive && F.State != FState.Juggle && F.State != FState.Grabbed)
            {
                Phase = p;
                StartCoroutine(PhaseTransition(p));
                return;
            }
            // combo breaker: only after a long combo, at most once every few seconds (it used to fire almost every time at 4 hits)
            if ((F.State == FState.Hitstun || F.State == FState.Juggle) && F.ComboTaken >= 6 + (Tune.breakChance < 0.1f ? 2 : 0)
                && Time.time >= nextBreakAt)
            {
                nextBreakAt = Time.time + 0.8f; // one roll per window, not one per AI tick
                if (Random.value < Tune.breakChance + 0.15f) { nextBreakAt = Time.time + 6f; F.Press(Cmd.Dodge); return; }
            }
            // final phase: the boss unleashes its ultimate on a timer (telegraphed by banner, shout and warning zones)
            if (FinalPhase && F.Kit.Ult != null && Time.time >= ultReadyAt && F.CanAct && F.Target != null && F.Target.Alive
                && F.Target.State != FState.Cinematic && F.Target.State != FState.Grabbed)
            {
                F.Rage = 100f;
                if (F.Kit.UseUlt())
                {
                    ultReadyAt = Time.time + ultEvery * Tune.bossPace;
                    patternReadyAt = Time.time + 3.5f;
                    return;
                }
            }
            if (F.CanAct && F.Target != null && Time.time >= patternReadyAt)
            {
                if (DoPattern(DistToTarget))
                {
                    patternReadyAt = Time.time + patternGap * Tune.bossPace * Mathf.Lerp(1.35f, 0.7f, Tune.aggression) * (1f - Phase * 0.06f);
                    return;
                }
            }
            base.Think();
            bool hurt = F.State == FState.Hitstun || F.State == FState.Juggle || F.State == FState.Knockdown || F.State == FState.GetUp || F.State == FState.Stunned;
            JustRecovered = wasHurt && !hurt && F.CanAct;
            wasHurt = hurt;
            if (!transitioning && !busy && F.Target != null) Style(DistToTarget);
        }

        /// <summary>Per-boss movement character applied on top of the shared brain (spacing, evasion, pressure).</summary>
        protected virtual void Style(float d) { }

        /// <summary>True on the first think after the boss recovers from a hit, a knockdown or a stun.</summary>
        protected bool JustRecovered;
        bool wasHurt;

        IEnumerator PhaseTransition(int p)
        {
            transitioning = true;
            moveDir = Vector3.zero;
            yield return new WaitForSeconds(0.15f);
            F.EnterCinematic();
            F.SetInvulnerable(2.4f);
            F.PlayRoar();
            Sfx.Play(SfxId.Roar, 1f, 0.8f + p * 0.1f);
            Fx.Ring(F.transform.position + Vector3.up * 0.15f, F.Kit.AuraColor, 0.5f, 12f, 0.8f, Quaternion.identity);
            Fx.Burst(F.Center, F.Kit.AuraColor, 3f, 0.6f);
            // phase roar pushes the player away (breaks their combo) but barely hurts
            Combat.AreaDamage(F, F.Team, F.Center, 3.2f, 2f, 10f, 2f, true, F.Kit.AuraColor, false, HitKind.Heavy);
            CameraRig.Cinematic(F.Center + Vector3.up * 0.4f, (F.Target != null ? KMath.Flat(F.Target.transform.position - F.transform.position).normalized : Vector3.back) + Vector3.up * 0.1f, 4.5f, 0.4f, 1.8f);
            CameraRig.Shake(0.6f);
            Haptics.Heavy();
            Hud.Banner("ФАЗА " + (p + 1), F.Kit.AuraColor);
            if (p - 1 < PhaseLines.Length) F.Shout(PhaseLines[p - 1]);
            OnPhase(p);
            yield return new WaitForSeconds(1.8f);
            F.EndRoar();
            F.ExitCinematic();
            F.MoveSpeed = speedBase * (1f + p * 0.05f);
            transitioning = false;
            patternReadyAt = Time.time + 1.6f * Tune.bossPace; // breathing room after the roar
            if (FinalPhase) ultReadyAt = Time.time + 5f * Tune.bossPace;
        }

        protected virtual void OnPhase(int p) { }
        protected abstract bool DoPattern(float dist);

        protected bool Tech(int i)
        {
            if (i < 0 || i >= F.Kit.Techs.Length) return false;
            return F.Kit.UseTech(i);
        }

        protected void RunPattern(IEnumerator routine) { StartCoroutine(Busy(routine)); }

        IEnumerator Busy(IEnumerator r)
        {
            busy = true;
            yield return StartCoroutine(r);
            busy = false;
        }

        /// <summary>Launcher -> chase jump -> air chain -> slam, executed like a player would.</summary>
        protected IEnumerator AirCombo()
        {
            if (F.Kit.Launcher == null) yield break;
            F.StartMove(F.Kit.Launcher);
            yield return new WaitForSeconds(F.Kit.Launcher.Total * 0.8f);
            if (F.Target == null || F.Target.State != FState.Juggle) yield break;
            F.Press(Cmd.Light); // chase jump
            yield return new WaitForSeconds(0.32f);
            for (int i = 0; i < 3 && F.Target != null && F.Target.State == FState.Juggle; i++)
            {
                F.Press(Cmd.Light);
                yield return new WaitForSeconds(0.24f);
            }
            F.Press(Cmd.Heavy);
            yield return new WaitForSeconds(0.5f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            busy = false; transitioning = false;
        }
    }
}
