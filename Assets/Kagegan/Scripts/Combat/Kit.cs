using System.Collections;
using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// A fighter's move set: basic chains, launcher/air combos, grabs, counter, 4 techniques, ultimate, finisher,
    /// and hooks for unique mechanics (absorption, modes, summons).
    /// </summary>
    public abstract class Kit
    {
        public Fighter F;
        public Move[] LightChain;
        public Move[] AirChain;
        public Move Heavy, Launcher, DashAttack, AirFinisher, Counter, Grab, Throw;
        public Technique[] Techs = new Technique[0];
        public Technique Ult;
        public string IntroLine = "", WinLine = "", UltLine = "";
        public Color AuraColor = Color.white;

        public virtual void Setup(Fighter f) { F = f; }
        public virtual void Tick(float dt) { }

        /// <summary>Return true if the hit was absorbed (no damage).</summary>
        public virtual bool TryAbsorb(ref HitInfo h) => false;
        /// <summary>Counter stances: return true to cancel the incoming hit (treated as a parry).</summary>
        public virtual bool Intercept(ref HitInfo h) => false;
        public virtual void OnDamaged(ref HitInfo h) { }
        public virtual void OnHitLanded(Fighter target, ref HitInfo h) { }
        /// <summary>Called before damage is applied to a target this kit's fighter hit; may scale h.Damage.</summary>
        public virtual void BeforeHitLanded(Fighter target, ref HitInfo h) { }
        public virtual void OnDeath() { }
        /// <summary>Called when a dodge starts (acrobatic kits swap the roll for flips, add afterimages).</summary>
        public virtual void OnDodge(Vector3 dir) { }
        /// <summary>Called after a frame-perfect dodge (the attack whiffed through the dodge window).</summary>
        public virtual void OnPerfectDodge(Fighter attacker) { }
        /// <summary>Called after a successful parry.</summary>
        public virtual void OnParry(Fighter attacker) { }
        /// <summary>Techniques temporarily unusable (overheat).</summary>
        public virtual bool TechsLocked => false;
        /// <summary>Optional resource gauge text for the HUD (heat, overheat...). Empty hides it.</summary>
        public virtual string GaugeText => "";

        /// <summary>Set by <see cref="Run"/> when a chained move was cut off (hit, death, state change).</summary>
        protected bool RunInterrupted;

        /// <summary>Plays one move of a scripted chain and waits for it to finish; sets <see cref="RunInterrupted"/> if it didn't.</summary>
        protected IEnumerator Run(Move m)
        {
            if (!F.Alive || (F.State != FState.Idle && F.State != FState.Attack)) { RunInterrupted = true; yield break; }
            F.StartMove(m);
            F.BufferLockUntil = Time.time + m.Total / Mathf.Max(0.1f, m.AnimSpeed * SpeedMul) + 0.05f;
            while (F.Alive && F.State == FState.Attack && F.CurMove == m) yield return null;
            RunInterrupted = !(F.Alive && F.State == FState.Idle);
            if (RunInterrupted) F.BufferLockUntil = 0f;
        }

        /// <summary>Damage multiplier applied to outgoing hits (modes, buffs).</summary>
        public virtual float OutgoingMul(Move m) => 1f;
        public virtual float SpeedMul => 1f;

        public bool UseTech(int i)
        {
            if (i < 0 || i >= Techs.Length) return false;
            var t = Techs[i];
            if (t == null || TechsLocked || !t.Ready(F) || !F.CanAct || F.Hacked) return false;
            if (t.Use == null || !t.Use(F)) return false;
            if (!F.InfiniteChakra) F.Chakra -= t.Cost;
            t.Cd = F.NoCooldowns ? 0.2f : t.Cooldown;
            Sfx.Play(SfxId.Charge, 0.35f, 1.4f);
            if ((F.IsPlayer || F.IsBoss) && !string.IsNullOrEmpty(t.Name)) Hud.MoveName(t.Name, t.Color, !F.IsPlayer, false);
            return true;
        }

        public bool UseUlt()
        {
            if (Ult == null || F.Rage < 100f || !F.CanAct || (F.Hacked && !F.IsBoss)) return false;
            if (Ult.Use == null || !Ult.Use(F)) return false;
            F.Rage = 0f;
            if ((F.IsPlayer || F.IsBoss) && !string.IsNullOrEmpty(Ult.Name)) Hud.MoveName(Ult.Name, Ult.Color, !F.IsPlayer, true);
            return true;
        }

        public void TickCooldowns(float dt)
        {
            for (int i = 0; i < Techs.Length; i++) if (Techs[i] != null && Techs[i].Cd > 0f) Techs[i].Cd -= dt;
        }

        public virtual bool CanFinish(Fighter t)
        {
            if (t == null || !t.Alive || t.Team == F.Team) return false;
            if (t.IsBoss && (t.Brain == null || !t.Brain.FinalPhase)) return false;
            float threshold = t.IsBoss ? 0.1f : 0.18f;
            if (t.Hp > t.MaxHp * threshold) return false;
            if (t.State != FState.Knockdown && t.State != FState.Stunned && t.State != FState.Hitstun && t.State != FState.Juggle) return false;
            return KMath.Flat(t.transform.position - F.transform.position).magnitude < 4f;
        }

        /// <summary>Cinematic finishing move; must end by killing the target.</summary>
        public virtual IEnumerator Finisher(Fighter t)
        {
            yield return F.StartCoroutine(Fighter.FinisherDefault(F, t));
        }
    }
}
