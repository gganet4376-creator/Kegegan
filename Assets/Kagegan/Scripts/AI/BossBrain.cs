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

        public override bool FinalPhase => Phase >= Thresholds.Length;
        public bool Transitioning => transitioning;

        public override void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
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
            if ((F.State == FState.Hitstun || F.State == FState.Juggle) && F.ComboTaken >= 4 && Random.value < Tune.breakChance + 0.25f)
            {
                F.Press(Cmd.Dodge);
                return;
            }
            if (F.CanAct && F.Target != null && Time.time >= patternReadyAt)
            {
                if (DoPattern(DistToTarget))
                {
                    patternReadyAt = Time.time + patternGap * Tune.bossPace * Mathf.Lerp(1.35f, 0.7f, Tune.aggression) * (1f - Phase * 0.1f);
                    return;
                }
            }
            base.Think();
        }

        IEnumerator PhaseTransition(int p)
        {
            transitioning = true;
            moveDir = Vector3.zero;
            yield return new WaitForSeconds(0.15f);
            F.EnterCinematic();
            F.SetInvulnerable(2.4f);
            F.ForceAnimMode(AnimMode.Custom, PoseLib.Roar);
            Sfx.Play(SfxId.Roar, 1f, 0.8f + p * 0.1f);
            Fx.Ring(F.transform.position + Vector3.up * 0.15f, F.Kit.AuraColor, 0.5f, 12f, 0.8f, Quaternion.identity);
            Fx.Burst(F.Center, F.Kit.AuraColor, 3f, 0.6f);
            Combat.AreaDamage(F, F.Team, F.Center, 3.2f, 6f, 12f, 3f, true, F.Kit.AuraColor, false, HitKind.Heavy);
            CameraRig.Cinematic(F.Center + Vector3.up * 0.4f, (F.Target != null ? KMath.Flat(F.Target.transform.position - F.transform.position).normalized : Vector3.back) + Vector3.up * 0.1f, 4.5f, 0.4f, 1.8f);
            CameraRig.Shake(0.6f);
            Haptics.Heavy();
            Hud.Banner("ФАЗА " + (p + 1), F.Kit.AuraColor);
            if (p - 1 < PhaseLines.Length) F.Shout(PhaseLines[p - 1]);
            OnPhase(p);
            yield return new WaitForSeconds(1.8f);
            F.ForceAnimMode(AnimMode.Locomotion);
            F.ExitCinematic();
            F.MoveSpeed = speedBase * (1f + p * 0.1f);
            transitioning = false;
            patternReadyAt = Time.time + 0.6f;
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
