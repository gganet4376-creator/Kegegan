using UnityEngine;

namespace Kagegan
{
    public enum Archetype { Brawler, Striker, Ranged, Heavy, Clone, Boss }

    /// <summary>
    /// Enemy / clone AI. Thinks a few times per second (cheap), drives the Fighter with the same commands as the player.
    /// Uses attack tokens so only a limited number of enemies press the player at once, reacts to attacks with a
    /// difficulty-dependent delay (block / parry / dodge), dodges projectiles and breaks combos.
    /// </summary>
    public class AIBrain : MonoBehaviour
    {
        public Fighter F;
        public Archetype Type;
        public DifficultyTuning Tune;
        public float PreferredRange = 1.4f;
        public bool UsesTokens = true;
        public bool Active = true;
        protected float thinkTimer;
        protected Vector3 moveDir;
        protected float strafeSign = 1f;
        protected float blockUntil;
        protected bool hasToken;
        protected int comboLeft;
        protected float nextAttackTime;
        protected float reactTimer = -1f;
        protected Fighter lastSeenAttacker;
        protected float retreatUntil;
        static int slotCounter;
        protected float flankAngle;
        float punishCheck;
        int seenAttacks;
        float seenAttacksDecay;
        Move lastSeenMove;
        bool blockedRecently;

        public virtual bool FinalPhase => false;

        public virtual void Setup(Fighter f, Archetype type, DifficultyTuning tune)
        {
            F = f; Type = type; Tune = tune;
            F.Brain = this;
            strafeSign = Random.value < 0.5f ? -1f : 1f;
            // each minion takes its own angle around the target so groups surround instead of stacking
            flankAngle = (slotCounter++ % 6) * 60f + Random.Range(-12f, 12f);
            UsesTokens = type != Archetype.Clone && type != Archetype.Boss;
            PreferredRange = type == Archetype.Ranged ? 7f : type == Archetype.Heavy ? 1.6f : 1.3f;
            nextAttackTime = Time.time + Random.Range(0.6f, 1.6f);
        }

        protected virtual void Update()
        {
            if (F == null || !F.Alive) { ReleaseToken(); return; }
            if (!Active || TimeFx.Paused) { F.SetMove(Vector3.zero); F.SetBlock(false); return; }
            float dt = Time.deltaTime;
            thinkTimer -= dt;
            if (thinkTimer <= 0f)
            {
                thinkTimer = Random.Range(0.12f, 0.22f);
                PickTarget();
                Think();
            }
            React(dt);
            F.SetMove(moveDir);
            F.SetBlock(Time.time < blockUntil);
        }

        protected void PickTarget()
        {
            if (F.Target != null && F.Target.Alive && F.Target.Team != F.Team && Random.value < 0.85f) return;
            F.Target = Combat.Nearest(F.transform.position, F.Team);
        }

        protected float DistToTarget => F.Target != null ? KMath.Flat(F.Target.transform.position - F.transform.position).magnitude : 999f;
        protected Vector3 DirToTarget => F.Target != null ? KMath.Flat(F.Target.transform.position - F.transform.position).normalized : F.transform.forward;

        protected virtual void Think()
        {
            var t = F.Target;
            moveDir = Vector3.zero;
            if (t == null) { ReleaseToken(); return; }
            if (F.State == FState.Hitstun || F.State == FState.Juggle)
            {
                if (Random.value < Tune.breakChance * 0.35f && F.ComboTaken >= 3) F.Press(Cmd.Dodge);
                return;
            }
            if (!F.CanAct) return;

            float d = DistToTarget;
            bool engage = !UsesTokens || hasToken || TryTakeToken();

            if (Time.time < retreatUntil)
            {
                moveDir = -DirToTarget + Vector3.Cross(Vector3.up, DirToTarget) * strafeSign * 0.5f;
                return;
            }

            if (Type == Archetype.Ranged)
            {
                RangedLogic(d);
                return;
            }

            if (!engage)
            {
                // hold a flanking slot around the target while waiting for an attack token
                float ang = flankAngle + Mathf.Sin(Time.time * 0.3f + flankAngle) * 25f;
                Vector3 slot = t.transform.position + Quaternion.Euler(0, ang, 0) * Vector3.forward * 4.3f;
                Vector3 toSlot = KMath.Flat(slot - F.transform.position);
                moveDir = toSlot.magnitude > 0.6f ? toSlot.normalized * Mathf.Clamp01(toSlot.magnitude / 2f) : Vector3.zero;
                if (Random.value < 0.035f) TryTechnique(d);
                return;
            }

            // wounded minions back off and fight from range
            if (Type != Archetype.Boss && Type != Archetype.Clone && F.HpFrac < 0.25f && d < 3f && Random.value < 0.25f)
            {
                retreatUntil = Time.time + Random.Range(0.6f, 1.2f);
                ReleaseToken();
                return;
            }

            // punish a whiffed attack: the target is stuck in recovery right next to us
            if (t.State == FState.Attack && t.CurMove != null && t.MoveTime > t.CurMove.Startup + t.CurMove.Active && d < 2.4f && F.CanAct && Random.value < 0.35f + Tune.aggression * 0.4f)
            {
                F.Press(Random.value < 0.45f ? Cmd.Heavy : Cmd.Light);
                comboLeft = Random.Range(1, 3);
                nextAttackTime = Time.time + 0.1f;
                return;
            }

            // after soaking a blow on the guard, hit back immediately
            if (blockedRecently && d < 2.2f)
            {
                blockedRecently = false;
                F.Press(F.CounterReady ? Cmd.Light : (Random.value < 0.5f ? Cmd.Light : Cmd.Grab));
                comboLeft = 2;
                nextAttackTime = Time.time + 0.12f;
                return;
            }

            if (d > PreferredRange + 0.4f)
            {
                moveDir = DirToTarget;
                if (d > 6f && Random.value < 0.08f) TryTechnique(d);
                return;
            }

            // in range
            if (Time.time >= nextAttackTime)
            {
                if (comboLeft <= 0)
                {
                    comboLeft = Random.Range(2, Type == Archetype.Heavy ? 3 : 5);
                    if (t.IsBlocking && Random.value < 0.6f && F.Kit.Grab != null) { F.Press(Cmd.Grab); comboLeft = 0; Pause(); return; }
                    if (Random.value < 0.25f && TryTechnique(d)) { comboLeft = 0; Pause(); return; }
                }
                if (comboLeft > 0)
                {
                    comboLeft--;
                    // finishers vary: heavy ender, launcher (heavy after 2 lights) or a throw against a turtling target
                    bool heavy = comboLeft == 0 && Random.value < 0.55f;
                    if (comboLeft == 0 && t.IsBlocking && Random.value < 0.4f && F.Kit.Grab != null) { F.Press(Cmd.Grab); Pause(); return; }
                    F.Press(heavy ? Cmd.Heavy : Cmd.Light);
                    if (comboLeft == 0) Pause();
                    else nextAttackTime = Time.time + 0.08f;
                }
            }
            else
            {
                // footsies: small strafe
                moveDir = Vector3.Cross(Vector3.up, DirToTarget) * strafeSign * 0.4f;
            }
        }

        protected void Pause()
        {
            float aggro = Mathf.Lerp(1.6f, 0.45f, Tune.aggression);
            nextAttackTime = Time.time + Random.Range(aggro * 0.6f, aggro * 1.4f);
            if (Random.value < 0.3f) retreatUntil = Time.time + Random.Range(0.3f, 0.8f);
            if (UsesTokens && Random.value < 0.5f) ReleaseToken();
        }

        void RangedLogic(float d)
        {
            Vector3 side = Vector3.Cross(Vector3.up, DirToTarget) * strafeSign;
            if (d < 4f) moveDir = -DirToTarget + side * 0.4f;
            else if (d > 10f) moveDir = DirToTarget;
            else moveDir = side * 0.7f;
            if (Random.value < 0.04f) strafeSign = -strafeSign;
            if (Time.time >= nextAttackTime && d < 13f)
            {
                if (!TryTechnique(d) && d < 2f) F.Press(Cmd.Light);
                nextAttackTime = Time.time + Random.Range(1.2f, 2.6f) * Mathf.Lerp(1.4f, 0.7f, Tune.aggression);
            }
        }

        protected bool TryTechnique(float d)
        {
            var techs = F.Kit.Techs;
            if (techs.Length == 0) return false;
            int start = Random.Range(0, techs.Length);
            for (int k = 0; k < techs.Length; k++)
            {
                int i = (start + k) % techs.Length;
                var tq = techs[i];
                if (tq == null || !tq.AiUsable || !tq.Ready(F)) continue;
                if (tq.Offensive && d > tq.Range) continue;
                F.Press(Cmd.Tech, i);
                return true;
            }
            return false;
        }

        /// <summary>Defensive reactions with a human-like delay.</summary>
        protected virtual void React(float dt)
        {
            var t = F.Target;
            if (t == null || F.State == FState.Dead) return;
            bool threat = t.InStartup && t.CurMove != null && t.CurMove.Damage > 0f && DistToTarget < t.CurMove.Range + 1.2f;
            // adapt: a target that keeps attacking gets blocked more often
            if (t.State == FState.Attack && t.CurMove != lastSeenMove) { lastSeenMove = t.CurMove; seenAttacks++; }
            seenAttacksDecay += dt;
            if (seenAttacksDecay > 2.5f) { seenAttacksDecay = 0f; seenAttacks = Mathf.Max(0, seenAttacks - 3); }
            if (F.State == FState.Block && t.State == FState.Attack && t.MoveTime > t.CurMove?.Startup) blockedRecently = true;
            if (threat && reactTimer < 0f && lastSeenAttacker != t) { reactTimer = Tune.reaction * Random.Range(0.8f, 1.3f); lastSeenAttacker = t; }
            if (!threat && t.State != FState.Attack) lastSeenAttacker = null;
            if (reactTimer >= 0f)
            {
                reactTimer -= dt;
                if (reactTimer < 0f && t.State == FState.Attack && (F.State == FState.Idle || F.State == FState.Block))
                {
                    float r = Random.value - Mathf.Min(0.2f, seenAttacks * 0.03f);
                    if (t.CurMove != null && t.CurMove.Kind == HitKind.Grab) { if (r < Tune.blockChance) F.Press(Cmd.Dodge); }
                    else if (r < Tune.parryChance) blockUntil = Time.time + 0.35f;     // fresh block = parry window
                    else if (r < Tune.blockChance) blockUntil = Time.time + Random.Range(0.4f, 0.9f);
                    else if (r < Tune.blockChance + 0.12f) F.Press(Cmd.Dodge);
                }
            }
            // dodge incoming projectiles
            if (Time.frameCount % 6 == 0 && F.CanAct)
            {
                for (int i = 0; i < Projectile.Live.Count; i++)
                {
                    var p = Projectile.Live[i];
                    if (p.Team == F.Team) continue;
                    Vector3 to = F.Center - p.transform.position;
                    if (to.magnitude > 6f || Vector3.Dot(to.normalized, p.Vel.normalized) < 0.9f) continue;
                    if (Random.value < Tune.blockChance) { if (Random.value < 0.5f) blockUntil = Time.time + 0.6f; else F.Press(Cmd.Dodge); }
                    break;
                }
            }
        }

        protected bool TryTakeToken()
        {
            if (hasToken) return true;
            if (Combat.AttackTokensInUse < Tune.attackTokens) { Combat.AttackTokensInUse++; hasToken = true; return true; }
            return false;
        }

        protected void ReleaseToken()
        {
            if (!hasToken) return;
            hasToken = false;
            Combat.AttackTokensInUse = Mathf.Max(0, Combat.AttackTokensInUse - 1);
        }

        protected virtual void OnDisable() { ReleaseToken(); }
    }
}
