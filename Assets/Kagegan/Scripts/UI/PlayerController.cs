using UnityEngine;

namespace Kagegan
{
    /// <summary>
    /// Maps touch controls, keyboard and gamepad (legacy Input Manager axes / joystick buttons) to Fighter commands.
    /// Techniques live in a fan menu ("Техники" opens every technique of the fighter); the gamepad keeps two slots
    /// with pages (the page button cycles through pairs). Also handles hold-type techniques and target lock-on.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public Fighter F;
        public TouchControls Touch;
        public int TechPage;
        bool blockTouch, blockKey;
        readonly bool[] techHeld = new bool[TouchControls.FanMax];
        float lockCheck;

        // ---- auto battle ("Авто" button): AI fights for AutoTime seconds, then the button cools down ----
        public const float AutoTime = 20f, AutoCooldown = 40f;
        AutoPilotBrain autoBrain;
        float autoUntil, autoReadyAt;
        public bool AutoOn => autoBrain != null && autoBrain.enabled;

        public void Bind(Fighter f, TouchControls touch)
        {
            StopAuto(false);
            if (autoBrain != null) { Destroy(autoBrain); autoBrain = null; }
            // a swap sends the previous fighter's Capri back into the net
            if (F != null && F != f && F.Kit is ReinaKit oldRk) oldRk.DespawnCapri();
            F = f; Touch = touch;
            if (Touch != null && Touch.Buttons.TryGetValue("capri", out var capBtn)) capBtn.gameObject.SetActive(f != null && f.Kit is ReinaKit);
            TechPage = 0;
            autoReadyAt = 0f;
            if (Touch != null) { Touch.OnButton = OnTouch; Touch.OnFan = Tech; Touch.SetFan(false); }
            RefreshTechLabels();
            if (f != null && PadInput.ConnectedCount > 0 && !Save.Data.padHintShown)
            {
                Save.Data.padHintShown = true;
                Hud.Popup("Геймпад подключён. Курки и крестовина: Настройки → Геймпад", new Color(0.6f, 0.9f, 1f));
            }
        }

        public int SlotToTech(int slot) => TechPage * 2 + slot;

        void OnTouch(string id, bool down)
        {
            if (F == null || TimeFx.Paused) return;
            if (id == "auto") { if (down) ToggleAuto(); return; }
            if (AutoOn) return; // the AI has the controls
            switch (id)
            {
                case "light": if (down) F.Press(Cmd.Light); break;
                case "heavy": if (down) F.Press(Cmd.Heavy); break;
                case "block": blockTouch = down; break;
                case "dodge": if (down) F.Press(Cmd.Dodge); break;
                case "jump": if (down) F.Press(Cmd.Jump); break;
                case "grab": if (down) F.Press(Cmd.Grab); break;
                case "techs": if (down) { Touch.ToggleFan(); Sfx.Play(SfxId.Click, 0.6f, Touch.FanOpen ? 1.2f : 0.9f); Haptics.Light(); } break;
                case "ult": if (down) F.Press(Cmd.Ult); break;
                case "capri":
                    if (down && F.Kit is ReinaKit rk)
                    {
                        if (rk.SummonCapri()) Haptics.Light();
                        else Sfx.Play(SfxId.Click, 0.4f, 0.7f);
                    }
                    break;
                case "ctx":
                    if (!down) break;
                    if (F.CanBreakFree) F.Press(Cmd.Dodge);   // every tap fills the struggle meter
                    else if (!F.TryTrip()) F.TryMountNearby();
                    break;
            }
        }

        void ToggleAuto()
        {
            if (AutoOn) { StopAuto(true); Sfx.Play(SfxId.Click, 0.6f); return; }
            if (Time.time < autoReadyAt || !F.Alive) { Sfx.Play(SfxId.Click, 0.3f, 0.6f); return; }
            blockTouch = false;
            for (int i = 0; i < techHeld.Length; i++) if (techHeld[i]) { techHeld[i] = false; F.SetTechHeld(i, false); }
            if (autoBrain == null) { autoBrain = F.gameObject.AddComponent<AutoPilotBrain>(); autoBrain.Init(F); }
            else autoBrain.enabled = true;
            autoUntil = Time.time + AutoTime;
            Sfx.Play(SfxId.Click, 0.8f, 1.3f);
            Hud.Popup("АВТО-БОЙ", new Color(0.55f, 0.9f, 1f));
        }

        void StopAuto(bool startCooldown)
        {
            if (!AutoOn) return;
            autoBrain.enabled = false;
            if (F != null) { F.Brain = null; F.SetMove(Vector3.zero); F.SetBlock(false); }
            if (startCooldown) autoReadyAt = Time.time + AutoCooldown;
        }

        void Tech(int i, bool down)
        {
            if (F == null || TimeFx.Paused || (AutoOn && down)) return;
            if (i >= F.Kit.Techs.Length || i >= techHeld.Length) return;
            var t = F.Kit.Techs[i];
            if (t == null) return;
            if (t.Hold)
            {
                techHeld[i] = down;
                F.SetTechHeld(i, down);
            }
            else if (down) F.Press(Cmd.Tech, i);
        }

        public void RefreshTechLabels()
        {
            if (Touch == null || F == null) return;
            Touch.FanCount = Mathf.Min(F.Kit.Techs.Length, TouchControls.FanMax);
            Touch.InvalidateFan();
            for (int i = 0; i < Touch.FanCount; i++)
            {
                var b = Touch.Fan[i];
                if (F.Kit.Techs[i] != null)
                {
                    var t = F.Kit.Techs[i];
                    b.Label.text = t.Short ?? t.Name;
                    if (b.Ring != null) { var rc = t.Color; rc.a = 1f; b.Ring.color = rc; }
                    if (b.Glyph != null) { b.Glyph.sprite = UIKit.Icon(t.Icon); b.Glyph.enabled = b.Glyph.sprite != null; }
                }
                else { b.Label.text = "—"; if (b.Glyph != null) b.Glyph.enabled = false; }
            }
            if (Touch.Buttons.TryGetValue("ult", out var u) && u.Glyph != null && F.Kit.Ult != null)
            {
                var sp = UIKit.Icon(F.Kit.Ult.Icon);
                if (sp != null) { u.Glyph.sprite = sp; u.Glyph.enabled = true; }
            }
        }

        void Update()
        {
            if (F == null || !F.Alive || AutoTest.Active) return;
            if (AutoOn)
            {
                if (Time.time >= autoUntil) StopAuto(true);
                else
                {
                    if (CameraRig.Inst != null) CameraRig.Inst.Target = F.Target != null ? F.Target.transform : null;
                    UpdateCooldowns();
                    return;
                }
            }
            if (TimeFx.Paused) { F.SetMove(Vector3.zero); return; }

            // ---- movement ----
            Vector2 stick = Touch != null && Touch.Root.gameObject.activeInHierarchy ? Touch.Stick.Value : Vector2.zero;
            if (legacyInputOk)
            {
                try
                {
                    Vector2 keys = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                    if (keys.sqrMagnitude > stick.sqrMagnitude) stick = Vector2.ClampMagnitude(keys, 1f);
                    if (keys.sqrMagnitude > 0.1f && PadInput.ConnectedCount > 0 && !Input.anyKey) PadInput.MarkActive();
                    ReadButtons();
                }
                catch (System.Exception) { legacyInputOk = false; Debug.LogWarning("Kagegan: legacy Input Manager disabled — keyboard/gamepad off, touch still works."); }
            }
            Vector3 world = CameraRig.Inst != null ? CameraRig.Inst.ToWorld(stick) : new Vector3(stick.x, 0, stick.y);
            F.SetMove(world);
            F.SetBlock(blockTouch || blockKey);

            // ---- targeting ----
            lockCheck -= Time.deltaTime;
            AutoFocusAttacker();
            if (F.Target == null || !F.Target.Alive || F.Target.Team == F.Team || lockCheck <= 0f)
            {
                lockCheck = 0.5f;
                if (F.Target == null || !F.Target.Alive || KMath.Flat(F.Target.transform.position - F.transform.position).magnitude > 14f)
                    F.Target = Combat.Nearest(F.transform.position, F.Team);
            }
            if (CameraRig.Inst != null) CameraRig.Inst.Target = F.Target != null ? F.Target.transform : null;

            UpdateCooldowns();
        }

        float focusHoldUntil;

        /// <summary>
        /// Auto-focus: swing the lock-on to whoever is attacking the player. A manual lock (target button) is respected
        /// for 3 s unless that other enemy actually lands a hit; mid-combo the lock only moves on a landed hit.
        /// </summary>
        void AutoFocusAttacker()
        {
            if (Time.time < focusHoldUntil) return;
            var cur = F.Target;
            bool curValid = cur != null && cur.Alive && cur.Team != F.Team;
            bool hitNow = F.LastAttacker != null && F.LastAttacker.Alive && Time.time - F.LastAttackedTime < 0.25f;
            bool comboing = curValid && F.ComboDealt >= 2 && (cur.State == FState.Hitstun || cur.State == FState.Juggle || cur.State == FState.Knockdown);
            bool manual = lockCheck > 0.5f;
            Fighter threat = (manual || comboing) ? (hitNow ? F.LastAttacker : null) : F.IncomingAttacker();
            if (threat == null || threat == cur) return;
            // the current target is also swinging at us: keep it (no ping-pong between two attackers)
            if (curValid && cur.Target == F && (cur.InStartup || cur.InActiveFrames)) return;
            F.Target = threat;
            focusHoldUntil = Time.time + 0.7f;
            lockCheck = Mathf.Max(lockCheck, 0.5f);
        }

        bool legacyInputOk = true;

        void ReadButtons()
        {
            // keyboard
            if (Input.GetKeyDown(KeyCode.J)) F.Press(Cmd.Light);
            if (Input.GetKeyDown(KeyCode.K)) F.Press(Cmd.Heavy);
            if (Input.GetKeyDown(KeyCode.Space)) F.Press(Cmd.Jump);
            if (Input.GetKeyDown(KeyCode.L)) F.Press(Cmd.Dodge);
            if (Input.GetKeyDown(KeyCode.U)) F.Press(Cmd.Grab);
            if (Input.GetKeyDown(KeyCode.R)) F.Press(Cmd.Ult);
            if (Input.GetKeyDown(KeyCode.Tab)) Touch?.ToggleFan();
            if (Input.GetKeyDown(KeyCode.T)) CycleTarget();
            KeyTech(KeyCode.Alpha1, 0); KeyTech(KeyCode.Alpha2, 1); KeyTech(KeyCode.Alpha3, 2); KeyTech(KeyCode.Alpha4, 3); KeyTech(KeyCode.Alpha5, 4); KeyTech(KeyCode.Alpha6, 5);

            // gamepad (remappable, see PadInput)
            PadInput.Poll();
            if (PadInput.Down(PadAction.Light)) F.Press(Cmd.Light);
            if (PadInput.Down(PadAction.Heavy)) F.Press(Cmd.Heavy);
            if (PadInput.Down(PadAction.Jump)) F.Press(Cmd.Jump);
            if (PadInput.Down(PadAction.Dodge)) F.Press(Cmd.Dodge); // also mashes out of grabs
            if (PadInput.Down(PadAction.Grab)) { if (!F.TryTrip() && !F.TryMountNearby()) F.Press(Cmd.Grab); }
            if (PadInput.Down(PadAction.Ult)) F.Press(Cmd.Ult);
            if (PadInput.Down(PadAction.TechPage)) SwapPage();
            if (PadInput.Down(PadAction.Target)) CycleTarget();
            if (PadInput.Down(PadAction.Capri) && F.Kit is ReinaKit rk) { if (rk.SummonCapri()) Haptics.Light(); }
            PadTech(PadAction.SlotA, SlotToTech(0));
            PadTech(PadAction.SlotB, SlotToTech(1));
            PadTech(PadAction.Tech1, 0); PadTech(PadAction.Tech2, 1); PadTech(PadAction.Tech3, 2); PadTech(PadAction.Tech4, 3);
            blockKey = Input.GetKey(KeyCode.LeftShift) || PadInput.Held(PadAction.Block);
        }

        int PageCount => Mathf.Max(1, (Mathf.Min(F.Kit.Techs.Length, TouchControls.FanMax) + 1) / 2);

        void SwapPage()
        {
            TechPage = (TechPage + 1) % PageCount;
            Sfx.Play(SfxId.Click, 0.6f);
            var a = SlotToTech(0) < F.Kit.Techs.Length ? F.Kit.Techs[SlotToTech(0)] : null;
            var b = SlotToTech(1) < F.Kit.Techs.Length ? F.Kit.Techs[SlotToTech(1)] : null;
            Hud.Popup("Техники: " + (a != null ? a.Short ?? a.Name : "—") + " / " + (b != null ? b.Short ?? b.Name : "—"), new Color(0.75f, 0.6f, 1f));
        }

        void KeyTech(KeyCode k, int i)
        {
            if (i >= F.Kit.Techs.Length) return;
            var t = F.Kit.Techs[i];
            if (t == null) return;
            if (t.Hold)
            {
                if (Input.GetKeyDown(k)) F.SetTechHeld(i, true);
                if (Input.GetKeyUp(k)) F.SetTechHeld(i, false);
            }
            else if (Input.GetKeyDown(k)) F.Press(Cmd.Tech, i);
        }

        void PadTech(PadAction a, int i)
        {
            if (i >= F.Kit.Techs.Length) return;
            var t = F.Kit.Techs[i];
            if (t == null) return;
            if (t.Hold)
            {
                if (PadInput.Down(a)) F.SetTechHeld(i, true);
                if (PadInput.Up(a)) F.SetTechHeld(i, false);
            }
            else if (PadInput.Down(a)) F.Press(Cmd.Tech, i);
        }

        public void CycleTarget()
        {
            Fighter best = null;
            float bd = float.MaxValue;
            foreach (var f in Fighter.All)
            {
                if (!f.Alive || f.Team == F.Team || f.Team == Team.Neutral || f == F.Target) continue;
                float d = (f.transform.position - F.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = f; }
            }
            if (best != null) { F.Target = best; lockCheck = 3f; Sfx.Play(SfxId.Click, 0.6f, 1.4f); }
        }

        readonly int[] techSecs = new int[TouchControls.FanMax];

        void UpdateCooldowns()
        {
            if (Touch == null) return;
            int ready = 0;
            for (int i = 0; i < Touch.FanCount && i < F.Kit.Techs.Length; i++)
            {
                var b = Touch.Fan[i];
                var t = F.Kit.Techs[i];
                if (t == null) continue;
                float cd = t.Cooldown > 0f ? Mathf.Clamp01(t.Cd / t.Cooldown) : 0f;
                bool affordable = F.InfiniteChakra || F.Chakra >= t.Cost;
                if (affordable && cd <= 0f && !F.Kit.TechsLocked) ready++;
                if (!b.gameObject.activeSelf) continue;
                b.CooldownFill.fillAmount = F.Kit.TechsLocked ? 1f : affordable ? cd : 1f;
                // name stays put; seconds go over the icon, and an unusable technique is visibly greyed out
                string nm = t.Short ?? t.Name;
                if (b.Label.text != nm) b.Label.text = nm;
                int secs = t.Cd > 0.05f ? Mathf.CeilToInt(t.Cd) : 0;
                bool usable = secs == 0 && affordable && !F.Kit.TechsLocked;
                if (b.Timer != null)
                {
                    if (b.Timer.enabled != (secs > 0)) b.Timer.enabled = secs > 0;
                    if (secs > 0 && techSecs[i] != secs) b.Timer.text = secs.ToString();
                }
                techSecs[i] = secs;
                if (b.Glyph != null) b.Glyph.color = new Color(1f, 1f, 1f, usable ? 0.95f : secs > 0 ? 0.18f : 0.4f);
                b.Label.color = usable ? Color.white : new Color(0.68f, 0.68f, 0.74f, 1f);
            }
            if (Touch.Buttons.TryGetValue("techs", out var tb))
            {
                tb.Label.text = F.Kit.TechsLocked ? "Блок" : "Техники " + ready;
                if (!Touch.FanOpen && tb.Ring != null) tb.Ring.color = ready > 0 ? new Color(0.72f, 0.58f, 1f) : new Color(0.35f, 0.32f, 0.42f);
            }
            if (Touch.Buttons.TryGetValue("ult", out var u))
            {
                u.CooldownFill.fillAmount = 1f - F.Rage / 100f;
                u.Label.text = F.Rage >= 100f ? (Mathf.Sin(Time.unscaledTime * 8f) > 0f ? "УЛЬТА!" : "УЛЬТА") : "УЛЬТА";
            }
            if (Touch.Capri != null && Touch.Capri.gameObject.activeSelf && F.Kit is ReinaKit rkit)
            {
                rkit.CapriState(out float cfill, out bool cready, out float cleft);
                Touch.Capri.Refresh(cfill, cready, cleft, rkit.Capri != null);
            }
            if (Touch.Buttons.TryGetValue("auto", out var a))
            {
                if (AutoOn)
                {
                    a.CooldownFill.fillAmount = 0f;
                    a.Label.text = "СТОП " + Mathf.CeilToInt(autoUntil - Time.time);
                    if (a.Ring != null) a.Ring.color = Color.Lerp(new Color(0.4f, 0.85f, 1f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
                }
                else
                {
                    float left = autoReadyAt - Time.time;
                    a.CooldownFill.fillAmount = left > 0f ? left / AutoCooldown : 0f;
                    a.Label.text = left > 0f ? Mathf.CeilToInt(left) + "с" : "АВТО";
                    if (a.Ring != null) a.Ring.color = new Color(0.4f, 0.85f, 1f);
                }
            }
            if (Touch.Buttons.TryGetValue("heavy", out var h))
                h.Label.text = F.Kit.CanFinish(F.Target) ? "ДОБИТЬ" : F.State == FState.Mount ? "Добить" : "Мощный";
            if (Touch.Buttons.TryGetValue("grab", out var g))
                g.Label.text = F.CanMountTarget ? "Верхом" : "Захват";
            Touch.SetContext(AutoOn ? null : F.CanBreakFree ? "Вырваться" : F.CanTrip ? "Подножка"
                : F.MountCandidate != null ? "Лечь и бить" : null);
        }

        void OnDisable()
        {
            StopAuto(false);
            for (int i = 0; i < techHeld.Length; i++) if (techHeld[i] && F != null) { techHeld[i] = false; F.SetTechHeld(i, false); }
            blockTouch = false;
            Touch?.SetFan(false);
        }
    }
}
