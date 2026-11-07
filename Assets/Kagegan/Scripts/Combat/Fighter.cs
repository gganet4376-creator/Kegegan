using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    public enum FState { Idle, Attack, Block, Dodge, Hitstun, Juggle, Knockdown, GetUp, Grabbed, Stunned, Channel, Cinematic, Dead, Mount }
    public enum Cmd { None, Light, Heavy, Jump, Dodge, Grab, Tech, Ult }

    public class FighterStats
    {
        public float damageDealt, damageTaken;
        public int hitsLanded, hitsTaken, parries, perfectDodges, maxCombo, kills, absorbs, finishers;
    }

    /// <summary>
    /// Core combat actor used by the player, clones, minions and bosses.
    /// Controllers (player input / AI) only issue commands; all rules live here.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class Fighter : MonoBehaviour
    {
        public static readonly List<Fighter> All = new List<Fighter>(16);

        // ---------- identity / stats ----------
        public string DisplayName = "Fighter";
        public Team Team;
        public Kit Kit;
        public HumanoidRig Rig;
        public ProcAnimator Anim;
        public CharacterController Cc;
        public AIBrain Brain;
        public FighterStats Stats = new FighterStats();
        public Color HitColor = new Color(1f, 0.85f, 0.5f);

        public float MaxHp = 500f, Hp = 500f;
        public float MaxStamina = 100f, Stamina = 100f;
        public float MaxChakra = 100f, Chakra = 100f;
        public float Rage;
        public float MoveSpeed = 6.2f, JumpPower = 9.5f, Weight = 1f;
        public float DamageMul = 1f, DefenseMul = 1f;
        public float ChakraRegen = 4f, StaminaRegen = 24f;
        public float BodyRadius = 0.36f, Height = 1.75f;
        public bool IsBoss, IsClone, IsDummy, IsPlayer, InfiniteChakra, NoCooldowns;

        // ---------- runtime ----------
        public FState State { get; private set; }
        public Fighter Target;
        public bool Grounded { get; private set; } = true;
        public Vector3 Velocity;
        Vector3 knock;
        public Move CurMove { get; private set; }
        public float MoveTime { get; private set; }
        public bool InActiveFrames => State == FState.Attack && CurMove != null && MoveTime >= CurMove.Startup && MoveTime < CurMove.Startup + CurMove.Active;
        public bool InStartup => State == FState.Attack && CurMove != null && MoveTime < CurMove.Startup;
        bool activeFired;
        bool envHitDone;
        readonly List<Fighter> hitList = new List<Fighter>(8);
        float rehitTimer;
        Move[] chain;
        int chainIndex;
        float chainOpenUntil;

        Vector3 moveInput;
        bool blockHeld;
        Cmd buffered;
        int bufferedTech;
        float bufferTime;

        float stateTimer;
        float invulnUntil, blockStart = -10f, counterUntil, dodgeStart, lastBreak = -10f, chaseUntil, lastHitTakenTime;
        Vector3 dodgeDir;
        bool airJumpUsed;
        bool knockdownOnLand;
        bool slamming;
        int juggleHits;
        public float DamageReduction;
        public bool SuperArmor;
        public float SlowMul = 1f;
        float slowUntil;
        float drainPerSec, drainUntil;
        Fighter drainer;
        public int ComboTaken { get; private set; }
        public int ComboDealt { get; private set; }
        float comboDealtTimer;
        public float LastHitDamage { get; private set; }
        public Fighter ThrowVictim;
        public Fighter GrabbedBy { get; private set; }
        Move throwMove;
        float flashAmount;
        Color flashColor = Color.white;
        Transform blob;

        public bool Alive => State != FState.Dead;
        public bool CanAct => State == FState.Idle || State == FState.Block || (State == FState.Attack && CurMove != null && MoveTime >= CurMove.Total * CurMove.CancelAt && !InActiveFrames);
        public bool Invulnerable => Time.time < invulnUntil;
        public bool IsBlocking => State == FState.Block;
        public bool CounterReady => Time.time < counterUntil;
        public Vector3 Center => transform.position + Vector3.up * Height * 0.55f;
        public Vector3 Head => transform.position + Vector3.up * Height * 0.92f;
        public float HpFrac => Hp / MaxHp;

        public event System.Action<Fighter, Fighter> OnDied;
        public event System.Action<Fighter, float> OnDamaged;
        public System.Action<string> OnShout;

        // ================== setup ==================
        public void Init(Kit kit, HumanoidRig rig, ProcAnimator anim)
        {
            Kit = kit; Rig = rig; Anim = anim;
            Cc = GetComponent<CharacterController>();
            Cc.height = Height;
            Cc.radius = Mathf.Min(0.32f * Height / 1.75f, Height * 0.45f);
            Cc.center = new Vector3(0, Height * 0.5f, 0);
            Cc.stepOffset = 0.35f;
            Cc.slopeLimit = 50f;
            Cc.skinWidth = 0.04f;
            Cc.minMoveDistance = 0f;
            Hp = MaxHp;
            kit.Setup(this);
            blob = BlobShadow.Create(transform, BodyRadius * 2.6f);
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        // ================== commands ==================
        public void SetMove(Vector3 worldDir) { moveInput = Vector3.ClampMagnitude(KMath.Flat(worldDir), 1f); }
        public void SetBlock(bool held)
        {
            if (held && !blockHeld) blockStart = Time.time;
            blockHeld = held;
        }
        public void Press(Cmd c, int tech = -1) { buffered = c; bufferedTech = tech; bufferTime = Time.time; }
        public void SetTechHeld(int i, bool held)
        {
            if (Kit == null || i < 0 || i >= Kit.Techs.Length) return;
            var t = Kit.Techs[i];
            if (t != null && t.Hold && t.Held != null) t.Held(this, held);
        }
        public void Shout(string line) { if (!string.IsNullOrEmpty(line)) OnShout?.Invoke(line); }

        // ================== main loop ==================
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (State == FState.Dead) { UpdateAnim(); ApplyPhysics(dt, false); return; }

            Regen(dt);
            Kit?.TickCooldowns(dt);
            Kit?.Tick(dt);
            if (comboDealtTimer > 0f) { comboDealtTimer -= dt; if (comboDealtTimer <= 0f) ComboDealt = 0; }
            if (flashAmount > 0f) { flashAmount = Mathf.Max(0f, flashAmount - dt * 6f); Rig.SetFlash(flashAmount, flashColor); }

            bool control = false;
            switch (State)
            {
                case FState.Idle:
                    control = true;
                    if (blockHeld && Grounded) EnterBlock();
                    else ConsumeBuffer();
                    break;
                case FState.Block:
                    control = true;
                    if (!blockHeld) { SetState(FState.Idle); Anim.StopAction(); }
                    else if (buffered == Cmd.Light && CounterReady) ConsumeBuffer();
                    else if (buffered == Cmd.Dodge || buffered == Cmd.Tech || buffered == Cmd.Ult || buffered == Cmd.Jump) { SetState(FState.Idle); ConsumeBuffer(); }
                    break;
                case FState.Attack: UpdateAttack(dt); break;
                case FState.Dodge: UpdateDodge(dt); break;
                case FState.Hitstun:
                    stateTimer -= dt;
                    if (buffered == Cmd.Dodge) TryComboBreak();
                    if (stateTimer <= 0f && Grounded) Recover();
                    break;
                case FState.Juggle:
                    if (buffered == Cmd.Dodge) TryComboBreak();
                    break;
                case FState.Knockdown:
                    stateTimer -= dt;
                    if (pinnedBy != null && pinnedBy.State == FState.Mount && pinnedBy.MountVictim == this) { stateTimer = Mathf.Max(stateTimer, 0.3f); break; }
                    pinnedBy = null;
                    if (buffered == Cmd.Dodge && stateTimer < 0.6f) { buffered = Cmd.None; stateTimer = 0f; }
                    if (stateTimer <= 0f) BeginGetUp();
                    break;
                case FState.GetUp:
                    stateTimer -= dt;
                    if (stateTimer <= 0f) Recover();
                    break;
                case FState.Stunned:
                    stateTimer -= dt;
                    if (stateTimer <= 0f) Recover();
                    break;
                case FState.Grabbed:
                    if (GrabbedBy == null || !GrabbedBy.Alive || GrabbedBy.ThrowVictim != this) { GrabbedBy = null; Recover(); }
                    break;
                case FState.Channel:
                    control = true;
                    break;
                case FState.Mount:
                    UpdateMount(dt);
                    break;
            }

            if (Time.time - bufferTime > 0.3f) buffered = Cmd.None;

            if (State != FState.Grabbed && State != FState.Cinematic) ApplyPhysics(dt, control);
            Face(dt);
            if (ThrowVictim != null) HoldVictim();
            UpdateAnim();
        }

        void Regen(float dt)
        {
            if (InfiniteChakra) Chakra = MaxChakra;
            else
            {
                Chakra = Mathf.Min(MaxChakra, Chakra + ChakraRegen * dt);
                if (Time.time < drainUntil)
                {
                    float d = Mathf.Min(Chakra, drainPerSec * dt);
                    Chakra -= d;
                    if (drainer != null && drainer.Alive) drainer.Chakra = Mathf.Min(drainer.MaxChakra, drainer.Chakra + d);
                }
            }
            float sr = State == FState.Block ? StaminaRegen * 0.25f : (State == FState.Attack || State == FState.Dodge ? StaminaRegen * 0.3f : StaminaRegen);
            Stamina = Mathf.Min(MaxStamina, Stamina + sr * dt);
            if (Time.time > slowUntil) SlowMul = 1f;
            if (IsDummy && Hp < MaxHp && Time.time - lastHitTakenTime > 2.5f) Hp = MaxHp;
        }

        // ================== input buffer ==================
        void ConsumeBuffer()
        {
            if (buffered == Cmd.None) return;
            var c = buffered;
            buffered = Cmd.None;
            switch (c)
            {
                case Cmd.Light: DoLight(); break;
                case Cmd.Heavy: DoHeavy(); break;
                case Cmd.Jump: DoJump(); break;
                case Cmd.Dodge: DoDodge(); break;
                case Cmd.Grab:
                    if (CanMount(Target)) BeginMount(Target);
                    else if (Grounded && Kit.Grab != null) StartMove(Kit.Grab);
                    break;
                case Cmd.Tech: Kit.UseTech(bufferedTech); break;
                case Cmd.Ult: Kit.UseUlt(); break;
            }
        }

        void DoLight()
        {
            if (CounterReady && Kit.Counter != null && Grounded) { counterUntil = 0f; StartMove(Kit.Counter); return; }
            if (!Grounded)
            {
                var ac = Kit.AirChain;
                if (ac == null || ac.Length == 0) return;
                int idx = (chain == ac && Time.time < chainOpenUntil) ? chainIndex + 1 : 0;
                if (idx >= ac.Length) return;
                StartMove(ac[idx], ac, idx);
                return;
            }
            if (Time.time < chaseUntil && Target != null && Target.State == FState.Juggle) { ChaseJump(); return; }
            var lc = Kit.LightChain;
            int i = (chain == lc && Time.time < chainOpenUntil) ? chainIndex + 1 : 0;
            if (i >= lc.Length) i = 0;
            StartMove(lc[i], lc, i);
        }

        void DoHeavy()
        {
            if (IsPlayer && Kit.CanFinish(Target)) { StartCoroutine(RunFinisher(Target)); return; }
            if (!Grounded) { if (Kit.AirFinisher != null) StartMove(Kit.AirFinisher); return; }
            if (CounterReady && Kit.Counter != null) { counterUntil = 0f; StartMove(Kit.Counter); return; }
            if (chain == Kit.LightChain && Time.time < chainOpenUntil && chainIndex >= 1 && Kit.Launcher != null) { StartMove(Kit.Launcher); return; }
            if (Kit.DashAttack != null && KMath.Flat(Velocity).magnitude > MoveSpeed * 0.8f) { StartMove(Kit.DashAttack); return; }
            if (Kit.Heavy != null) StartMove(Kit.Heavy);
        }

        void DoJump()
        {
            if (Grounded)
            {
                Velocity.y = JumpPower; Grounded = false; airJumpUsed = false;
                Sfx.Play(SfxId.Whoosh, 0.4f, 1.3f); Fx.Dust(transform.position, 0.6f, 4);
                if (!Anim.PlayMocap("NinjaJump_Start", 1.8f, 0.25f, false, 0.3f) && !Anim.PlayMocap("Jump_Start", 1.7f, 0.3f, false, 0.3f))
                    Anim.Play(Clip.Seq(0f, PoseLib.LandSquash, 0.07f, PoseLib.AirUp, 0.22f, null)); // push-off then tuck
            }
            else if (!airJumpUsed) { airJumpUsed = true; Velocity.y = JumpPower * 0.85f; Fx.Smoke(transform.position, 0.4f, new Color(1, 1, 1, 0.4f), 4); }
        }

        void ChaseJump()
        {
            // super jump up to a juggled target to continue the air combo
            Vector3 to = Target.transform.position - transform.position;
            float h = Mathf.Max(1.5f, Target.transform.position.y + 0.3f - transform.position.y);
            Velocity.y = Mathf.Sqrt(2f * 24f * h);
            float tUp = Velocity.y / 24f;
            Vector3 flat = KMath.Flat(to);
            Vector3 hv = flat.magnitude > 1.2f ? flat.normalized * (flat.magnitude - 0.9f) / tUp : Vector3.zero;
            Velocity.x = hv.x; Velocity.z = hv.z;
            Grounded = false;
            chaseUntil = 0f;
            Sfx.Play(SfxId.Whoosh, 0.6f, 0.8f);
            Fx.Dust(transform.position, 0.8f, 6);
        }

        void DoDodge()
        {
            if (Stamina < 14f || !Grounded) return;
            Stamina -= 14f;
            dodgeDir = moveInput.sqrMagnitude > 0.05f ? moveInput.normalized : -transform.forward;
            dodgeStart = Time.time;
            SetState(FState.Dodge);
            stateTimer = 0.32f;
            if (Vector3.Dot(dodgeDir, transform.forward) > 0.3f) Anim.PlayMocap("Slide_Start", 2.4f, 0.05f, false, 0.32f, 0.05f); // captured dash-slide
            Sfx.Play(SfxId.Whoosh, 0.6f, 1.5f);
            Fx.Dust(transform.position, 0.5f, 4);
        }

        void UpdateDodge(float dt)
        {
            stateTimer -= dt;
            float k = Mathf.Clamp01(stateTimer / 0.32f);
            Vector3 v = dodgeDir * MoveSpeed * 2.3f * (0.4f + k);
            Velocity.x = v.x; Velocity.z = v.z;
            if (stateTimer <= 0f) { SetState(FState.Idle); ConsumeBuffer(); }
        }

        public bool DodgeInvuln => State == FState.Dodge && Time.time - dodgeStart < 0.26f;

        void EnterBlock()
        {
            SetState(FState.Block);
            Anim.PlayMocap("Idle_Shield_Loop", 1f, 0.2f, false, 999f, 0.07f); // captured guard stance
        }

        // ================== moves ==================
        public void StartMove(Move m, Move[] fromChain = null, int index = 0)
        {
            if (m == null) return;
            if (m.ChakraCost > 0f && !InfiniteChakra)
            {
                if (Chakra < m.ChakraCost) return;
                Chakra -= m.ChakraCost;
            }
            Stamina = Mathf.Max(0f, Stamina - m.StaminaCost);
            CurMove = m;
            MoveTime = 0f;
            activeFired = false;
            envHitDone = false;
            hitList.Clear();
            rehitTimer = 0f;
            chain = fromChain;
            chainIndex = index;
            chainOpenUntil = 0f;
            SetState(FState.Attack);
            float speed = Kit != null ? Kit.SpeedMul : 1f;
            bool mocapPlayed = false;
            if (m.Mocap != null && Anim.HasMocap && Grounded)
            {
                // time-scale the captured strike so the limb is fully extended exactly on the first active frame
                float startup = Mathf.Max(0.05f, m.Startup / Mathf.Max(0.1f, m.AnimSpeed * speed));
                float clipSpeed = Mathf.Clamp(m.MocapContact / startup, 0.8f, 2.6f);
                float start = Mathf.Max(0f, m.MocapContact - startup * clipSpeed);
                mocapPlayed = Anim.PlayMocap(m.Mocap, clipSpeed, start, false, m.Total / Mathf.Max(0.1f, m.AnimSpeed * speed) + 0.05f, 0.06f);
            }
            if (!mocapPlayed) Anim.Play(m.Clip, m.AnimSpeed * speed);
            if (m.Airborne && Grounded) { Velocity.y = 6f; Grounded = false; }
            m.OnStart?.Invoke(this);
        }

        void UpdateAttack(float dt)
        {
            var m = CurMove;
            float speed = Kit != null ? Kit.SpeedMul : 1f;
            MoveTime += dt * m.AnimSpeed * speed;
            float t = MoveTime;
            m.OnTick?.Invoke(this, t / m.Total);

            // lunge
            if (t < m.Startup + m.Active && m.Lunge != 0f)
            {
                Vector3 fwd = transform.forward * m.Lunge;
                if (Target != null && m.Lunge > 0f)
                {
                    float d = KMath.Flat(Target.transform.position - transform.position).magnitude;
                    if (d < 1.0f) fwd *= 0.15f; // don't run through the target
                }
                Velocity.x = fwd.x; Velocity.z = fwd.z;
            }
            else { Velocity.x *= 0.8f; Velocity.z *= 0.8f; }

            if (t >= m.Startup && !activeFired)
            {
                activeFired = true;
                m.OnActive?.Invoke(this);
                if (State != FState.Attack || CurMove != m) return;
                if (m.Sfx != SfxId.Count) Sfx.PlayAt(m.Sfx, Center, m.Kind == HitKind.Light ? 0.45f : 0.7f, m.Kind == HitKind.Light ? 1.15f : 0.9f);
                if (m.SlashFx && m.Damage > 0f) Fx.Slash(transform, new Vector3(0, Height * 0.55f, 0.55f), m.SlashEuler, m.FxColor, (1.1f + m.Radius) * (m.Kind == HitKind.Light ? 0.65f : 1f));
            }

            if (InActiveFrames)
            {
                if (m.Hits > 1)
                {
                    rehitTimer += dt;
                    if (rehitTimer >= m.Active / m.Hits) { rehitTimer = 0f; hitList.Clear(); }
                }
                HitCheck(m);
                if (State != FState.Attack || CurMove != m) return;
            }

            if (t >= m.Total * m.CancelAt && t >= m.Startup + m.Active)
            {
                chainOpenUntil = Time.time + 0.35f;
                if (buffered != Cmd.None && buffered != Cmd.Jump) { EndMove(); ConsumeBuffer(); return; }
            }
            if (t >= m.Total)
            {
                EndMove();
                ConsumeBuffer();
            }
        }

        void EndMove()
        {
            var m = CurMove;
            if (State == FState.Attack) SetState(Grounded ? FState.Idle : FState.Idle);
            if (chain != null) chainOpenUntil = Time.time + 0.35f;
            m?.OnEnd?.Invoke(this);
        }

        public void CancelMove()
        {
            if (State == FState.Attack) { CurMove?.OnEnd?.Invoke(this); }
            if (State == FState.Mount) EndMount(false);
            Anim.StopAction();
            if (ThrowVictim != null) { ThrowVictim.ReleaseGrab(); ThrowVictim = null; }
        }

        public Vector3 BonePoint(HitBone b)
        {
            switch (b)
            {
                case HitBone.HandL: return Rig.BonePos(B.HandL);
                case HitBone.FootR: return Rig.BonePos(B.FootR);
                case HitBone.FootL: return Rig.BonePos(B.FootL);
                case HitBone.Body: return Rig.BonePos(B.Chest);
                case HitBone.Head: return Rig.BonePos(B.Head);
                default: return Rig.BonePos(B.HandR);
            }
        }

        public Vector3 HitPoint(Move m) => BonePoint(m.Bone) + transform.rotation * m.Reach;

        void HitCheck(Move m)
        {
            if (m.Damage <= 0f && m.Kind != HitKind.Grab) return;
            Vector3 a = Rig.BonePos(B.Chest);
            Vector3 b = HitPoint(m);
            for (int i = 0; i < All.Count; i++)
            {
                var f = All[i];
                if (f == this || !f.Alive || f.Team == Team || hitList.Contains(f)) continue;
                if ((f.State == FState.Knockdown || f.State == FState.GetUp) && !m.HitsDowned) continue;
                float d = KMath.SegmentSegmentDistance(a, b, f.transform.position + Vector3.up * 0.25f, f.transform.position + Vector3.up * (f.Height - 0.15f));
                if (d > m.Radius + f.BodyRadius) continue;

                // attack clash: both fighters are in active frames and their strikes meet
                if (f.InActiveFrames && f.CurMove.Kind != HitKind.Grab && m.Kind != HitKind.Grab && f.CurMove.Damage > 0f)
                {
                    Vector3 fb = f.HitPoint(f.CurMove);
                    if ((fb - b).magnitude < m.Radius + f.CurMove.Radius + 0.35f)
                    {
                        Clash(this, f, (fb + b) * 0.5f);
                        return;
                    }
                }
                hitList.Add(f);
                Vector3 dir = KMath.Flat(f.transform.position - transform.position).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
                var h = HitInfo.FromMove(this, m, b, dir);
                h.Damage *= DamageMul * (Kit != null ? Kit.OutgoingMul(m) : 1f);
                var res = f.ReceiveHit(ref h);
                if (res == HitResult.Hit || res == HitResult.Armored)
                {
                    Anim.Recoil(m.Kind == HitKind.Light ? 0.4f : 0.9f);
                    m.OnHit?.Invoke(this, f);
                    if (m.Launch > 0f && Grounded) chaseUntil = Time.time + 0.7f;
                }
                if (m.Kind == HitKind.Grab) break;
            }
            if (m.Kind != HitKind.Light && !envHitDone) { envHitDone = true; Destructible.ImpactAt(b, m.Radius + 0.4f, m.Damage * 1.5f, this); }
        }

        static void Clash(Fighter a, Fighter b, Vector3 point)
        {
            int pa = Move.Power(a.CurMove.Kind), pb = Move.Power(b.CurMove.Kind);
            Fx.Parry(point);
            Sfx.PlayAt(SfxId.Clash, point, 1f);
            TimeFx.HitStop(0.1f);
            CameraRig.Shake(0.3f);
            if (a.IsPlayer || b.IsPlayer) Haptics.Medium();
            Vector3 dir = KMath.Flat(b.transform.position - a.transform.position).normalized;
            if (pa == pb)
            {
                a.knock = -dir * 6f; b.knock = dir * 6f;
                a.Stun(0.22f); b.Stun(0.22f);
            }
            else
            {
                var loser = pa > pb ? b : a;
                var winner = pa > pb ? a : b;
                loser.knock = (loser == b ? dir : -dir) * 7f;
                loser.Stun(0.55f);
                winner.hitList.Remove(loser);
            }
        }

        // ================== receiving damage ==================
        public HitResult ReceiveHit(ref HitInfo h)
        {
            if (!Alive) return HitResult.Miss;
            if (State == FState.Cinematic && h.Kind != HitKind.Ultimate) return HitResult.Miss;
            if (DodgeInvuln)
            {
                if (Time.time - dodgeStart < 0.2f && h.Attacker != null)
                {
                    Stats.perfectDodges++;
                    counterUntil = Time.time + 1.1f;
                    TimeFx.SlowMo(0.45f, 0.3f);
                    Fx.Smoke(Center, 0.6f, new Color(0.7f, 0.85f, 1f, 0.5f), 6);
                    Sfx.Play(SfxId.Teleport, 0.7f);
                    if (IsPlayer) Hud.Popup("ИДЕАЛЬНОЕ УКЛОНЕНИЕ", new Color(0.6f, 0.9f, 1f));
                    return HitResult.PerfectDodge;
                }
                return HitResult.Miss;
            }
            if (Invulnerable) return HitResult.Miss;
            if (h.Kind != HitKind.Ultimate && h.Kind != HitKind.Environment && Kit != null && Kit.Intercept(ref h)) return HitResult.Parried;

            if (h.Absorbable && Kit != null && Kit.TryAbsorb(ref h))
            {
                Stats.absorbs++;
                return HitResult.Absorbed;
            }

            bool frontal = Vector3.Dot(transform.forward, -h.Dir) > -0.15f;
            if (State == FState.Block && !h.Unblockable && frontal && h.Kind != HitKind.Grab && h.Kind != HitKind.Ultimate)
            {
                if (Time.time - blockStart < 0.2f && h.Parryable)
                {
                    Stats.parries++;
                    counterUntil = Time.time + 0.9f;
                    Fx.Parry(h.Point);
                    Sfx.PlayAt(SfxId.Parry, h.Point, 1f);
                    TimeFx.HitStop(0.12f);
                    TimeFx.SlowMo(0.35f, 0.4f);
                    CameraRig.Shake(0.2f);
                    if (IsPlayer) { Haptics.Medium(); Hud.Popup("ПАРИРОВАНИЕ!", new Color(1f, 0.9f, 0.4f)); }
                    if (h.Attacker != null && h.Proj == null && (h.Attacker.transform.position - transform.position).sqrMagnitude < 16f)
                    {
                        h.Attacker.CancelMove();
                        h.Attacker.Stun(0.8f);
                    }
                    return HitResult.Parried;
                }
                float staminaDmg = h.Stamina * (h.Kind == HitKind.Heavy ? 1.6f : 1f) * (h.Kind >= HitKind.Technique ? 1.4f : 1f);
                Stamina -= staminaDmg;
                float chip = h.Kind >= HitKind.Technique ? h.Damage * 0.15f : 0f;
                if (chip > 0f) Hp = Mathf.Max(1f, Hp - chip);
                knock = h.Dir * Mathf.Min(h.Knockback, 4f) * 0.6f / Weight;
                Fx.Block(h.Point);
                Sfx.PlayAt(SfxId.Block, h.Point, 0.8f);
                TimeFx.HitStop(0.03f);
                if (IsPlayer) Haptics.Light();
                if (Stamina <= 0f)
                {
                    Stamina = 0f;
                    Stun(1.2f);
                    Fx.Hit(Center, new Color(0.6f, 0.8f, 1f), true);
                    Sfx.Play(SfxId.HeavyHit, 0.8f, 1.3f);
                    if (IsPlayer || (h.Attacker != null && h.Attacker.IsPlayer)) Hud.Popup("ЗАЩИТА ПРОБИТА", new Color(1f, 0.5f, 0.3f));
                }
                return HitResult.Blocked;
            }

            Kit?.OnDamaged(ref h);

            float combo = Mathf.Max(0.35f, 1f - ComboTaken * 0.06f);
            float dmg = h.Damage * DefenseMul * (1f - DamageReduction) * combo;
            Hp -= dmg;
            LastHitDamage = dmg;
            lastHitTakenTime = Time.time;
            ComboTaken++;
            Rage = Mathf.Min(100f, Rage + dmg * 0.45f);
            Stats.damageTaken += dmg; Stats.hitsTaken++;
            var atk = h.Attacker;
            if (atk != null)
            {
                atk.Rage = Mathf.Min(100f, atk.Rage + dmg * 0.3f);
                atk.Stats.damageDealt += dmg; atk.Stats.hitsLanded++;
                // a combo only continues while this target has not recovered since the previous hit
                bool reeling = ComboTaken > 1 && (State == FState.Hitstun || State == FState.Juggle || State == FState.Stunned || State == FState.Knockdown);
                atk.ComboDealt = reeling && atk.comboDealtTimer > 0f ? atk.ComboDealt + 1 : 1;
                atk.comboDealtTimer = 1.1f;
                if (atk.ComboDealt > atk.Stats.maxCombo) atk.Stats.maxCombo = atk.ComboDealt;
                atk.LastHitDamage = dmg;
                if (atk.Kit != null) atk.Kit.OnHitLanded(this, ref h);
                // clones count toward the player's combo
                if (atk.IsClone && atk.Owner != null) atk.Owner.Stats.damageDealt += dmg; // clone hits count as damage, not as the player's combo
            }
            flashAmount = h.Kind == HitKind.Light ? 0.35f : 0.55f; flashColor = new Color(1f, 0.92f, 0.85f);
            Fx.Hit(h.Point, h.FxColor, h.Kind != HitKind.Light);
            Sfx.Impact(dmg, h.Kind != HitKind.Light, h.Point);
            TimeFx.HitStop(h.HitStop);
            CameraRig.Shake(h.Shake);
            if (IsPlayer) Haptics.Medium();
            else if (atk != null && (atk.IsPlayer || atk.IsClone)) { if (h.Kind == HitKind.Light) Haptics.Light(); else Haptics.Heavy(); }
            OnDamaged?.Invoke(this, dmg);

            if (Hp <= 0f)
            {
                Hp = 0f;
                Die(atk, h);
                return HitResult.Hit;
            }

            bool armored = (SuperArmor || (State == FState.Attack && CurMove != null && CurMove.Armor)) && !h.Knockdown && h.Launch <= 0f && h.Kind != HitKind.Ultimate && h.Kind != HitKind.Grab && !h.IgnoreArmor;
            if (armored)
            {
                Anim.Flinch(0.4f, false);
                return HitResult.Armored;
            }

            CancelMove();
            if (h.Kind == HitKind.Grab) return HitResult.Hit; // grab handler takes over

            bool fromBehind = !frontal;
            float rel = (h.Point.y - transform.position.y) / Mathf.Max(0.5f, Height);
            int zone = rel > 0.78f ? 1 : rel < 0.42f ? 2 : 0;
            float side = Vector3.Dot(transform.right, h.Point - Center) >= 0f ? -1f : 1f;
            Anim.HurtPose = fromBehind ? PoseLib.HurtBack : zone == 1 ? PoseLib.HurtHead : zone == 2 ? PoseLib.HurtLow : (h.Kind == HitKind.Light && Random.value < 0.4f ? PoseLib.Hurt : PoseLib.HurtBody);
            Anim.Flinch(1f, fromBehind, zone, side);
            float kb = h.Knockback / Weight;
            if (h.Slam && !Grounded)
            {
                Velocity.y = -16f; knock = h.Dir * kb * 0.4f;
                knockdownOnLand = true; slamming = true;
                SetState(FState.Juggle);
            }
            else if (h.Launch > 0f || !Grounded || h.Knockdown)
            {
                float up = h.Launch > 0f ? h.Launch : (h.Knockdown ? 5f : 3.2f);
                if (juggleHits > 9 && !h.Knockdown) up *= 0.25f;
                Velocity.y = Mathf.Min(Mathf.Max(Velocity.y * 0.3f, up / Mathf.Sqrt(Weight)), 11f);
                if (transform.position.y > 2.6f) Velocity.y = Mathf.Min(Velocity.y, 2f); // keep juggles inside the frame
                knock = h.Dir * kb * (h.Knockdown ? 1.5f : 0.6f);
                juggleHits++;
                knockdownOnLand = h.Knockdown || juggleHits > 2 || !IsBoss;
                Grounded = false;
                SetState(FState.Juggle);
            }
            else
            {
                knock = h.Dir * kb;
                stateTimer = h.Hitstun * (IsBoss ? 0.6f : 1f);
                SetState(FState.Hitstun);
                // captured hit reaction, timed to the stun so the fighter is back in stance as control returns
                string react = kb >= 6f ? "Hit_Knockback" : zone == 1 ? "Hit_Head" : "Hit_Chest";
                var rc = MocapLibrary.Get(react);
                if (rc != null) Anim.PlayMocap(react, Mathf.Clamp(rc.length / Mathf.Max(0.15f, stateTimer), 0.7f, 1.6f), 0f, false, -1f, 0.05f);
            }
            return HitResult.Hit;
        }

        public void Stun(float seconds)
        {
            if (!Alive) return;
            CancelMove();
            stateTimer = seconds;
            SetState(FState.Stunned);
        }

        void TryComboBreak()
        {
            buffered = Cmd.None;
            if (Chakra < 30f || Stamina < 25f || Time.time - lastBreak < 3f) return;
            if (Brain == null && !IsPlayer) return;
            Chakra -= 30f; Stamina -= 25f; lastBreak = Time.time;
            Vector3 from = transform.position;
            Fx.Smoke(Center, 0.9f, new Color(0.95f, 0.95f, 0.95f, 0.7f), 14);
            Fx.Debris(Center, new Color(0.55f, 0.38f, 0.2f), 5, 3f); // the "substitution log"
            Sfx.Play(SfxId.Poof, 1f);
            Vector3 away = Target != null ? KMath.Flat(transform.position - Target.transform.position).normalized : -transform.forward;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            Teleport(from + away * 3.5f);
            Velocity = Vector3.zero; knock = Vector3.zero;
            invulnUntil = Time.time + 0.6f;
            ComboTaken = 0; juggleHits = 0;
            SetState(FState.Idle);
            Anim.StopAction();
            if (IsPlayer) Hud.Popup("ПОДМЕНА", new Color(0.8f, 0.9f, 1f));
        }

        public void Teleport(Vector3 pos)
        {
            pos = Arena.ClampInside(pos, 1f);
            Cc.enabled = false;
            transform.position = new Vector3(pos.x, Mathf.Max(0f, pos.y), pos.z);
            Cc.enabled = true;
        }

        void Recover()
        {
            ComboTaken = 0; juggleHits = 0;
            SetState(FState.Idle);
        }

        // ================== grabs ==================
        /// <summary>Called by a grab move on hit: holds the victim in front, then the kit's throw move releases them.</summary>
        public void BeginThrow(Fighter victim, Move toss)
        {
            if (victim == null || !victim.Alive) return;
            victim.CancelMove();
            victim.GrabbedBy = this;
            victim.SetState(FState.Grabbed);
            victim.Velocity = Vector3.zero;
            ThrowVictim = victim;
            throwMove = toss;
            StartMove(toss);
        }

        void HoldVictim()
        {
            var v = ThrowVictim;
            if (v == null || !v.Alive || v.State != FState.Grabbed) { ThrowVictim = null; return; }
            // the victim is pulled into the hold over a few frames (no snapping)
            Vector3 p = transform.position + transform.forward * 0.75f + Vector3.up * 0.2f;
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            v.Cc.enabled = false;
            v.transform.position = Vector3.Lerp(v.transform.position, p, k);
            v.transform.rotation = Quaternion.Slerp(v.transform.rotation, Quaternion.LookRotation(-transform.forward), k);
            v.Cc.enabled = true;
            if (State != FState.Attack || CurMove != throwMove) { v.ReleaseGrab(); ThrowVictim = null; }
        }

        /// <summary>Releases the held victim with the throw hit (called from the throw move's OnActive).</summary>
        public void ReleaseThrow(float damage, float knockback, float launch)
        {
            var v = ThrowVictim;
            if (v == null) return;
            ThrowVictim = null;
            v.GrabbedBy = null;
            v.SetState(FState.Idle);
            var h = new HitInfo
            {
                Attacker = this, Damage = damage * DamageMul, Stamina = 0, Knockback = knockback, Launch = launch, Hitstun = 0.6f,
                Kind = HitKind.Heavy, Unblockable = true, Knockdown = true, Parryable = false, IgnoreArmor = true,
                Point = v.Center, Dir = transform.forward, FxColor = HitColor, Shake = 0.35f, HitStop = 0.08f
            };
            v.ReceiveHit(ref h);
            Fx.Dust(v.transform.position, 1.2f, 10);
        }

        public void ReleaseGrab()
        {
            GrabbedBy = null;
            if (State == FState.Grabbed) SetState(FState.Idle);
        }

        // ================== debuffs ==================
        public void ApplyDrain(Fighter source, float perSec, float seconds, float slow)
        {
            drainer = source; drainPerSec = perSec; drainUntil = Time.time + seconds;
            SlowMul = Mathf.Min(SlowMul, slow); slowUntil = Time.time + seconds;
        }

        public bool Drained => Time.time < drainUntil;

        // ================== death ==================
        public Fighter Owner; // for clones / summons

        void Die(Fighter killer, HitInfo h)
        {
            CancelMove();
            SetState(FState.Dead);
            if (Grounded && Anim.PlayMocap("Death01", 1.15f, 0f, true, -1f, 0.08f))
            {
                // captured collapse on the spot, with a short shove from the final blow
                Anim.DeathMocap = true;
                knock = h.Dir * Mathf.Clamp(h.Knockback, 2f, 5f);
            }
            else
            {
                Velocity.y = Grounded ? 5f : Mathf.Max(Velocity.y, 2f);
                knock = h.Dir * Mathf.Max(5f, h.Knockback * 1.5f);
                Grounded = false;
            }
            if (killer != null) { killer.Stats.kills++; if (killer.IsClone && killer.Owner != null) killer.Owner.Stats.kills++; }
            Kit?.OnDeath();
            if (IsBoss || IsPlayer)
            {
                TimeFx.SlowMo(1.2f, 0.25f);
                Sfx.Play(SfxId.Ko, 1f);
                CameraRig.Shake(0.5f);
            }
            OnDied?.Invoke(this, killer);
        }

        public void Kill(Fighter killer)
        {
            if (!Alive) return;
            Hp = 0f;
            Die(killer, new HitInfo { Dir = killer != null ? KMath.Flat(transform.position - killer.transform.position).normalized : -transform.forward, Knockback = 6f });
        }

        // ================== finishers ==================
        IEnumerator RunFinisher(Fighter t)
        {
            SetState(FState.Cinematic);
            t.CancelMove();
            t.SetState(FState.Cinematic);
            Stats.finishers++;
            Hud.Popup("ДОБИВАНИЕ", new Color(1f, 0.35f, 0.3f));
            yield return StartCoroutine(Kit.Finisher(t));
            if (t.Alive) t.Kill(this);
            if (State == FState.Cinematic) SetState(FState.Idle);
            CameraRig.EndCinematic();
        }

        /// <summary>Generic cinematic finisher: dash in, rapid strikes, launcher, slam.</summary>
        public static IEnumerator FinisherDefault(Fighter f, Fighter t)
        {
            Vector3 side = Vector3.Cross(Vector3.up, KMath.Flat(t.transform.position - f.transform.position).normalized);
            CameraRig.Cinematic(t.Center, side + Vector3.back * 0.3f, 4.5f, 0.8f, 2.4f);
            TimeFx.SlowMo(2.2f, 0.6f);
            f.Teleport(t.transform.position - KMath.Flat(t.transform.position - f.transform.position).normalized * 1.0f);
            f.FaceInstant(t.transform.position);
            Pose[] seq = { PoseLib.JabR, PoseLib.JabL, PoseLib.HookR, PoseLib.KneeR, PoseLib.ElbowR };
            for (int i = 0; i < seq.Length; i++)
            {
                f.Anim.Play(Clip.Hold(seq[i], 0.05f, 0.06f, 0.05f));
                yield return new WaitForSeconds(0.12f);
                Fx.Hit(t.Center, f.HitColor, i >= 3);
                Sfx.Play(SfxId.Hit, 1f, 1f + i * 0.08f);
                t.Anim.Flinch(1f, false);
                t.flashAmount = 1f;
            }
            f.Anim.Play(Clip.Hold(PoseLib.Uppercut, 0.06f, 0.2f, 0.2f));
            yield return new WaitForSeconds(0.1f);
            t.Kill(f);
            t.Velocity.y = 12f;
            Fx.Explosion(t.Center, 2.5f, f.HitColor);
            Sfx.Play(SfxId.Explosion, 1f);
            CameraRig.Shake(0.7f);
            yield return new WaitForSeconds(0.6f);
        }

        // ================== cinematic control used by kits ==================
        public void EnterCinematic() { CancelMove(); SetState(FState.Cinematic); Velocity = Vector3.zero; knock = Vector3.zero; }
        public void ExitCinematic() { if (State == FState.Cinematic) SetState(FState.Idle); }
        public void EnterChannel() { CancelMove(); SetState(FState.Channel); }
        public void ExitChannel() { if (State == FState.Channel) SetState(FState.Idle); }
        public void GrantCounter(float seconds) { counterUntil = Time.time + seconds; }
        public void SetInvulnerable(float seconds) { invulnUntil = Mathf.Max(invulnUntil, Time.time + seconds); }
        public void AddKnock(Vector3 v) { knock += v; }
        public void Flash(Color c, float amount = 1f) { flashColor = c; flashAmount = amount; }

        public void SetState(FState s)
        {
            if (State == FState.Dead && s != FState.Dead) return;
            if (State == FState.Block && s != FState.Block && Anim != null && Anim.HasMocap) Anim.Mocap.StopAction(0.08f); // leave the guard loop
            if (State == FState.Attack && s != FState.Attack) { CurMove = null; }
            State = s;
            if (s == FState.Idle) { slamming = false; }
        }

        public void Revive(float hpFrac)
        {
            State = FState.Idle;
            if (Anim != null) { Anim.DeathMocap = false; Anim.StopAction(); }
            Hp = MaxHp * hpFrac;
            Velocity = Vector3.zero; knock = Vector3.zero;
            ComboTaken = 0; juggleHits = 0;
        }

        // ================== get-up variants ==================
        void BeginGetUp()
        {
            SetState(FState.GetUp);
            int v;
            if (Anim.Stance == StanceKind.Swift || Anim.Stance == StanceKind.Thunder) v = Random.value < 0.7f ? 1 : 2;
            else if (Anim.Stance == StanceKind.Heavy || IsBoss) v = Random.value < 0.75f ? 0 : 2;
            else v = Random.Range(0, 3);
            if (v != 1 && Anim.HasMocap && MocapLibrary.Get("LayToIdle") != null)
            {
                // captured get-up from lying on the back (same orientation as the knockdown pose)
                stateTimer = 0.72f;
                Anim.PlayMocap("LayToIdle", 1.53f / stateTimer, 0f, false, stateTimer, 0.12f);
                invulnUntil = Time.time + stateTimer + 0.15f;
                Fx.Dust(transform.position, 0.6f, 4);
                return;
            }
            switch (v)
            {
                case 1: // kip-up: spring back to the feet
                    stateTimer = 0.42f;
                    Anim.Play(Clip.Seq(0f, PoseLib.Down, 0.1f, PoseLib.KipTuck, 0.24f, PoseLib.AirUp, 0.42f, null));
                    Velocity.y = 3.2f; Grounded = false;
                    Sfx.Play(SfxId.Whoosh, 0.4f, 1.2f);
                    break;
                case 2: // backward roll away from the attacker
                    stateTimer = 0.55f;
                    Anim.Play(Clip.Seq(0f, PoseLib.Down, 0.12f, PoseLib.RollBack, 0.34f, PoseLib.Crouch, 0.55f, null));
                    knock = -transform.forward * 4.5f;
                    break;
                default: // push up through one knee
                    stateTimer = 0.5f;
                    Anim.Play(Clip.Seq(0f, PoseLib.Down, 0.15f, PoseLib.GetUpMid, 0.32f, PoseLib.OneKnee, 0.5f, null));
                    break;
            }
            invulnUntil = Time.time + stateTimer + 0.15f;
            Fx.Dust(transform.position, 0.6f, 4);
        }

        // ================== mount: pin a downed opponent and rain blows ==================
        public Fighter MountVictim { get; private set; }
        Fighter pinnedBy;
        int mountHits, mountMaxHits;
        Vector3 mountFrom, mountSeat;
        float mountEnter;
        float mountTimer, mountActionT = -1f, mountHitAt;
        bool mountRight, mountHammer;

        public bool CanMountTarget => IsPlayer && CanMount(Target);

        bool CanMount(Fighter t)
        {
            return t != null && t.Alive && t.Team != Team && t.State == FState.Knockdown && Grounded && State != FState.Mount
                   && KMath.Flat(t.transform.position - transform.position).magnitude < 2.2f;
        }

        void BeginMount(Fighter t)
        {
            CancelMove();
            MountVictim = t;
            t.pinnedBy = this;
            mountHits = 0;
            mountMaxHits = t.IsBoss ? 3 : 6;
            mountTimer = t.IsBoss ? 2.2f : 3.5f;
            mountActionT = -1f;
            SetState(FState.Mount);
            Velocity = Vector3.zero; knock = Vector3.zero;
            // the victim lies on its back with the head toward its -forward: sit across the hips facing the head
            // pounce onto the opponent along a short arc instead of snapping into place
            mountSeat = Arena.ClampInside(t.transform.position - t.transform.forward * 0.25f, 0.3f);
            mountFrom = transform.position;
            mountEnter = 0f;
            ForceAnimMode(AnimMode.Custom, PoseLib.MountSit);
            Anim.Play(Clip.Seq(0f, PoseLib.Crouch, 0.12f, PoseLib.AirUp, 0.24f, PoseLib.MountSit, 0.34f, PoseLib.MountSit));
            Vector3 side = Vector3.Cross(Vector3.up, transform.forward);
            CameraRig.Cinematic(t.transform.position + Vector3.up * 0.6f, side + Vector3.up * 0.15f, 4.2f, 1.6f, mountTimer + 0.6f);
            Sfx.PlayAt(SfxId.Land, transform.position, 0.8f, 0.9f);
            Fx.Dust(t.transform.position, 1f, 8);
            if (IsPlayer) { Haptics.Medium(); Hud.Popup("ВЕРХОМ!  Атака — удары, Мощный — добить", new Color(1f, 0.8f, 0.5f)); }
        }

        void UpdateMount(float dt)
        {
            var v = MountVictim;
            if (v == null || !v.Alive || v.State != FState.Knockdown) { EndMount(true); return; }
            if (mountEnter < 1f)
            {
                mountEnter = Mathf.Min(1f, mountEnter + dt / 0.28f);
                Vector3 p = Vector3.Lerp(mountFrom, mountSeat, mountEnter) + Vector3.up * Mathf.Sin(mountEnter * Mathf.PI) * 0.6f;
                Cc.enabled = false;
                transform.position = p;
                Vector3 face = KMath.Flat(-v.transform.forward);
                if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-20f * dt));
                Cc.enabled = true;
                if (mountEnter >= 1f) { Fx.Dust(v.transform.position, 0.8f, 6); Sfx.PlayAt(SfxId.Land, transform.position, 0.8f, 0.9f); }
                return;
            }
            mountTimer -= dt;
            if (mountActionT >= 0f)
            {
                mountActionT += dt;
                if (mountHitAt > 0f && mountActionT >= mountHitAt)
                {
                    mountHitAt = -1f;
                    float dmg = mountHammer ? 20f : 6.5f;
                    v.TakeGroundPound(this, dmg * DamageMul * (Kit != null ? Kit.OutgoingMul(null) : 1f), mountHammer);
                    mountHits++;
                    Anim.Recoil(mountHammer ? 1f : 0.5f);
                    if (mountHammer) { Fx.Crack(v.transform.position, 2.4f); Fx.Ring(v.transform.position + Vector3.up * 0.1f, HitColor, 0.4f, 4f, 0.4f, Quaternion.identity); CameraRig.Shake(0.55f); }
                }
                if (mountActionT >= (mountHammer ? 0.55f : 0.26f))
                {
                    mountActionT = -1f;
                    if (mountHammer || !v.Alive) { EndMount(true); return; }
                }
                return;
            }
            if (mountTimer <= 0f) { EndMount(true); return; }
            var c = buffered;
            if (c == Cmd.Heavy || (c == Cmd.Light && mountHits >= mountMaxHits - 1))
            {
                buffered = Cmd.None;
                mountHammer = true; mountActionT = 0f; mountHitAt = 0.3f;
                Anim.Play(Clip.Seq(0f, null, 0.18f, PoseLib.MountHammerWind, 0.3f, PoseLib.MountHammer, 0.55f, PoseLib.MountSit));
                Sfx.Play(SfxId.Whoosh, 0.7f, 0.8f);
            }
            else if (c == Cmd.Light)
            {
                buffered = Cmd.None;
                mountRight = !mountRight;
                mountHammer = false; mountActionT = 0f; mountHitAt = 0.12f;
                Anim.Play(Clip.Seq(0f, null, 0.07f, mountRight ? PoseLib.MountWindR : PoseLib.MountWindL, 0.12f, mountRight ? PoseLib.MountPunchR : PoseLib.MountPunchL, 0.26f, PoseLib.MountSit));
                Sfx.Play(SfxId.Whoosh, 0.45f, 1.2f);
            }
            else if (c == Cmd.Dodge || c == Cmd.Jump) { buffered = Cmd.None; EndMount(true); }
        }

        void EndMount(bool hopOff)
        {
            var v = MountVictim;
            MountVictim = null;
            if (v != null && v.pinnedBy == this) { v.pinnedBy = null; if (v.State == FState.Knockdown) v.stateTimer = Mathf.Min(v.stateTimer, 0.5f); }
            if (State == FState.Mount) SetState(FState.Idle);
            ForceAnimMode(AnimMode.Locomotion);
            Anim.StopAction();
            if (hopOff) { knock = -transform.forward * 3.5f; Velocity.y = 4f; Grounded = false; }
            CameraRig.EndCinematic();
        }

        /// <summary>Damage dealt to a pinned, downed fighter (mount blows): no launch, stays on the ground.</summary>
        public void TakeGroundPound(Fighter attacker, float dmg, bool heavy)
        {
            if (!Alive) return;
            dmg *= DefenseMul * (1f - DamageReduction) * Mathf.Max(0.5f, 1f - ComboTaken * 0.04f);
            Hp -= dmg;
            ComboTaken++;
            LastHitDamage = dmg;
            lastHitTakenTime = Time.time;
            Stats.damageTaken += dmg; Stats.hitsTaken++;
            Rage = Mathf.Min(100f, Rage + dmg * 0.45f);
            if (attacker != null)
            {
                attacker.Stats.damageDealt += dmg; attacker.Stats.hitsLanded++;
                attacker.ComboDealt++; attacker.comboDealtTimer = 1.3f;
                if (attacker.ComboDealt > attacker.Stats.maxCombo) attacker.Stats.maxCombo = attacker.ComboDealt;
                attacker.LastHitDamage = dmg;
                attacker.Rage = Mathf.Min(100f, attacker.Rage + dmg * 0.3f);
            }
            Vector3 p = Rig.BonePos(B.Head);
            Fx.Hit(p, attacker != null ? attacker.HitColor : Color.white, heavy);
            Sfx.Impact(heavy ? 30f : dmg, heavy, p);
            TimeFx.HitStop(heavy ? 0.1f : 0.04f);
            CameraRig.Shake(heavy ? 0.4f : 0.15f);
            if (attacker != null && attacker.IsPlayer) { if (heavy) Haptics.Heavy(); else Haptics.Light(); }
            flashAmount = 0.5f; flashColor = new Color(1f, 0.92f, 0.85f);
            Anim.Flinch(heavy ? 1f : 0.6f, false, 1, Random.value < 0.5f ? 1f : -1f);
            OnDamaged?.Invoke(this, dmg);
            if (Hp <= 0f)
            {
                Hp = 0f;
                Die(attacker, new HitInfo { Dir = attacker != null ? KMath.Flat(transform.position - attacker.transform.position).normalized : -transform.forward, Knockback = 2f });
            }
        }

        // ================== physics ==================
        void ApplyPhysics(float dt, bool control)
        {
            float gravity = -24f;
            if (State == FState.Juggle && juggleHits <= 9 && Time.time - lastHitTakenTime < 0.45f && Velocity.y < 2f) gravity *= 0.5f;
            if (State == FState.Attack && !Grounded && CurMove != null && Velocity.y < 0f) gravity *= 0.55f; // air combos hang briefly
            if (State == FState.Channel && !Grounded) gravity *= 0.3f;

            if (control && State != FState.Dodge)
            {
                float spd = MoveSpeed * SlowMul * (Kit != null ? Kit.SpeedMul : 1f) * (State == FState.Block ? 0.3f : 1f) * (State == FState.Channel ? 0.35f : 1f);
                Vector3 desired = moveInput * spd;
                float accel = Grounded ? 45f : 18f;
                Vector3 hv = KMath.Flat(Velocity);
                hv = Vector3.MoveTowards(hv, desired, accel * dt);
                Velocity.x = hv.x; Velocity.z = hv.z;
            }
            else if (State != FState.Attack && State != FState.Dodge)
            {
                Velocity.x = Mathf.MoveTowards(Velocity.x, 0f, 30f * dt);
                Velocity.z = Mathf.MoveTowards(Velocity.z, 0f, 30f * dt);
            }

            Velocity.y += gravity * dt;
            if ((State == FState.Juggle || State == FState.Dead) && transform.position.y > 3.4f && Velocity.y > 0f) Velocity.y = 0f;
            Vector3 total = Velocity + knock;
            knock = Vector3.MoveTowards(knock, Vector3.zero, (Grounded ? 18f : 6f) * dt);

            if (!Cc.enabled) return;
            var flags = Cc.Move(total * dt);
            bool wasGrounded = Grounded;
            Grounded = (flags & CollisionFlags.Below) != 0 || (Cc.isGrounded && Velocity.y <= 0f);
            if (Grounded && Velocity.y < 0f) Velocity.y = -2f;
            if ((flags & CollisionFlags.Above) != 0 && Velocity.y > 0f) Velocity.y = 0f;
            if ((flags & CollisionFlags.Sides) != 0 && knock.magnitude > 7f)
            {
                Destructible.ImpactAt(Center + knock.normalized * 0.6f, 1.2f, knock.magnitude * 6f, null);
                CameraRig.Shake(0.2f);
                knock *= 0.3f;
            }

            if (!wasGrounded && Grounded) Landed();
            SeparateFromOthers();
            if (transform.position.y < -5f) Teleport(new Vector3(0, 1f, 0));
        }

        void Landed()
        {
            airJumpUsed = false;
            if (State == FState.Juggle || State == FState.Dead)
            {
                Fx.Dust(transform.position, slamming ? 1.6f : 1f, slamming ? 14 : 7);
                Sfx.Play(SfxId.Land, 0.9f);
                if (slamming) { Fx.Crack(transform.position, 2.2f); CameraRig.Shake(0.35f); Destructible.ImpactAt(transform.position, 2f, 40f, null); }
                if (State == FState.Dead) return;
                if (knockdownOnLand || slamming)
                {
                    SetState(FState.Knockdown);
                    stateTimer = IsBoss ? 0.6f : 0.95f;
                    knock *= 0.3f;
                }
                else Recover();
                slamming = false;
            }
            else if (State == FState.Attack && CurMove != null && CurMove == Kit.AirFinisher)
            {
                Fx.Dust(transform.position, 1.2f, 10);
                Fx.Crack(transform.position, 1.6f);
            }
            else if (State != FState.Dodge)
            {
                Fx.Dust(transform.position, 0.5f, 3);
                if (State == FState.Idle)
                {
                    if (!Anim.PlayMocap("NinjaJump_Land", 1.8f, 0.1f, false, 0.32f) && !Anim.PlayMocap("Jump_Land", 1.8f, 0.12f, false, 0.32f))
                        Anim.Play(Clip.Seq(0f, null, 0.05f, PoseLib.LandSquash, 0.22f, null)); // absorb the landing
                    Sfx.PlayAt(SfxId.Land, transform.position, 0.35f, 1.2f);
                }
            }
        }

        void SeparateFromOthers()
        {
            if (State == FState.Grabbed || State == FState.Dead) return;
            Vector3 p = transform.position;
            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i];
                if (o == this || !o.Alive || o.State == FState.Grabbed || o.ThrowVictim == this || ThrowVictim == o || o == MountVictim || o.MountVictim == this) continue;
                Vector3 d = KMath.Flat(p - o.transform.position);
                float min = (BodyRadius + o.BodyRadius) * 1.2f;
                float m = d.magnitude;
                if (m < min && Mathf.Abs(p.y - o.transform.position.y) < 1.4f)
                {
                    // heavier fighters move less; a downed body is not shoved around
                    float share = o.State == FState.Knockdown ? 1f : o.Weight / (Weight + o.Weight);
                    Vector3 push = (m > 0.001f ? d / m : transform.right) * (min - m) * share;
                    Cc.Move(push);
                }
            }
        }

        // ================== facing ==================
        void Face(float dt)
        {
            Vector3 dir = Vector3.zero;
            float rate = 0f;
            switch (State)
            {
                case FState.Idle:
                case FState.Block:
                case FState.Channel:
                    if (Target != null && Target.Alive && (State != FState.Idle || moveInput.sqrMagnitude < 0.04f || KMath.Flat(Target.transform.position - transform.position).sqrMagnitude < 64f))
                    { dir = Target.transform.position - transform.position; rate = 14f; }
                    else if (moveInput.sqrMagnitude > 0.04f) { dir = moveInput; rate = 14f; }
                    break;
                case FState.Attack:
                    if (InStartup && Target != null && CurMove != null && !CurMove.NoTracking) { dir = Target.transform.position - transform.position; rate = 18f; }
                    break;
                case FState.Dodge:
                    break;
            }
            dir = KMath.Flat(dir);
            if (rate <= 0f || dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-rate * dt));
        }

        public void FaceInstant(Vector3 point)
        {
            Vector3 d = KMath.Flat(point - transform.position);
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d);
        }

        void UpdateAnim()
        {
            if (Anim == null) return;
            AnimMode mode;
            switch (State)
            {
                case FState.Block: mode = AnimMode.Block; break;
                case FState.Dodge: mode = AnimMode.Dodge; break;
                case FState.Hitstun: mode = AnimMode.Hurt; break;
                case FState.Juggle: mode = AnimMode.Juggled; break;
                case FState.Knockdown: mode = AnimMode.Down; break;
                case FState.GetUp: mode = AnimMode.Crouch; break;
                case FState.Grabbed: mode = AnimMode.Grabbed; break;
                case FState.Stunned: mode = AnimMode.Dizzy; break;
                case FState.Dead: mode = AnimMode.Dead; break;
                case FState.Mount: mode = AnimMode.Custom; break;
                default: mode = Grounded ? AnimMode.Locomotion : AnimMode.Air; break;
            }
            bool keepVictory = Anim.Mode == AnimMode.Victory && State == FState.Idle && moveInput.sqrMagnitude < 0.01f;
            bool keepCustom = Anim.Mode == AnimMode.Custom && (State == FState.Channel || State == FState.Cinematic || State == FState.Mount);
            if (!(keepVictory || keepCustom) || mode != AnimMode.Locomotion) Anim.Mode = mode;
            if (State == FState.Mount) Anim.CustomPose = PoseLib.MountSit;
            Anim.MoveSpeed = KMath.Flat(Velocity).magnitude;
            Anim.VerticalSpeed = Velocity.y;
            Anim.InCombat = Target != null && Target.Alive;
            Anim.Tired = 1f - Stamina / MaxStamina;
            if (Target != null && Target.Alive) Anim.LookAt(Target.Head); else Anim.ClearLook();
            if (blob != null) BlobShadow.Track(blob, transform.position, BodyRadius * 2.6f);
        }

        public void ForceAnimMode(AnimMode m, Pose custom = null)
        {
            Anim.Mode = m;
            if (custom != null) Anim.CustomPose = custom;
        }
    }
}
