using System;
using System.Globalization;
using System.IO;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(WalkingCoach.Core), "WalkingCoach", "1.0.1", "waze3174")]
[assembly: MelonGame]

namespace WalkingCoach;

// shows what nate's legs feel, to learn walking from: grip and slip under each foot, fatigue, how far you lean
// against the game's own fall rule, and where your weight is against your feet. the markers are drawn on a screen
// overlay where the feet show up on screen, so i don't need any of the game's shaders
public class Core : MelonMod
{
    MelonPreferences_Entry<bool> shown;
    MelonPreferences_Entry<KeyCode> toggleKey;

    public override void OnInitializeMelon()
    {
        string dir = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "WalkingCoach");
        Directory.CreateDirectory(dir);
        var cat = MelonPreferences.CreateCategory("WalkingCoach");
        cat.SetFilePath(Path.Combine(dir, "WalkingCoach.cfg"));
        shown = cat.CreateEntry("Shown", true, "Shown", "Coach on or off (also toggled in game with ToggleKey).");
        toggleKey = cat.CreateEntry("ToggleKey", KeyCode.F6, "Toggle key", "Key that shows / hides the coach.");
    }

    // ---- the game's fall rule ----
    // you stay up as long as one planted leg leans less than 55 degrees. past that, leaning over 30-40 degrees drops you in 1/8 s
    const float LegHoldsAngle = 55f;
    const float SteepSlope = 45f;             // slope colour: green on flat ground, red at this angle (grip's flat-ground bonus is gone by 30)
    const float LeanFrom = 30f;              // lean colour fades from green here to red at LegHoldsAngle
    // both feet planted more than 1.95 leg lengths apart is the splits, an instant fall
    const float SplitsAt = 1.95f, SplitsFrom = 1f, SplitsRed = 1.9f;   // line fades from white at 1 leg length to red at 1.9
    const float OutsideRed = 0.25f;           // weight this far (m) outside the feet = full red diamond

    // warning colour, smooth: 0 green, 0.5 yellow, 1 red
    static Color Ramp(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? Color.Lerp(Good, Warn, t * 2f) : Color.Lerp(Warn, Bad, (t - 0.5f) * 2f);
    }

    // white -> yellow -> red, for the lines between the feet
    static Color LineRamp(float spread)
    {
        float t = Mathf.Clamp01((spread - SplitsFrom) / (SplitsRed - SplitsFrom));
        return t < 0.5f ? Color.Lerp(Color.white, Warn, t * 2f) : Color.Lerp(Warn, Bad, (t - 0.5f) * 2f);
    }

    // grip colours. the game's grip per surface: wood 1.09, dirt 0.96, rock 0.89, grass 0.88, gravel / moss 0.73, mud 0.59,
    // ice 0.14, and x1.5 on flat ground
    static Color GripColor(float g) =>
        g >= 0.85f ? new Color(0.45f, 1f, 0.5f) :
        g >= 0.6f ? new Color(1f, 0.9f, 0.35f) :
        g >= 0.35f ? new Color(1f, 0.55f, 0.2f) :
        new Color(0.55f, 0.85f, 1f);   // ice

    static readonly Color Good = new Color(0.45f, 1f, 0.5f), Warn = new Color(1f, 0.85f, 0.3f), Bad = new Color(1f, 0.35f, 0.3f);

    // ---- overlay ----

    GameObject root;
    TextMeshProUGUI text;
    RectTransform[] footMark = new RectTransform[2];
    Image[] footImg = new Image[2];
    RectTransform comMark, supportLine;
    Image comImg, supportImg;
    Canvas canvas;

    bool errorLogged;

    public override void OnUpdate()
    {
        try
        {
            if (Input.GetKeyDown(toggleKey.Value)) { shown.Value = !shown.Value; MelonPreferences.Save(); }
            Draw();
        }
        catch (Exception e)
        {
            if (!errorLogged) { errorLogged = true; LoggerInstance.Error("coach: " + e); }
            if (root != null) root.SetActive(false);
        }
    }

    void Draw()
    {
        var pm = Il2Cpp.PlayerMovement.me;
        var menu = Il2Cpp.Menu.me;
        var cam = Camera.main;
        bool on = shown.Value && pm != null && cam != null && (menu == null || !menu.paused);
        if (!on) { if (root != null) root.SetActive(false); return; }
        if (root == null && !Make(menu)) return;
        root.SetActive(true);

        var feet = pm.feet;
        var hips = pm.hips;
        var sb = new System.Text.StringBuilder();
        if (pm.dead)
        {
            // fallen: keep the text, hide the ground markers
            for (int i = 0; i < 2; i++) footMark[i].gameObject.SetActive(false);
            supportLine.gameObject.SetActive(false);
            comMark.gameObject.SetActive(false);
            SetText($"<color=#{Hex(Bad)}>DOWN</color>\n");
            return;
        }
        Vector3 liftedPos = default; bool anyLifted = false;
        sb.Append(Header("FEET", "SLOPE", "GRIP", "FATIGUE"));

        // feet: grip, slip, fatigue
        Vector3 sum = Vector3.zero; int planted = 0;
        float bestLeg = 180f;
        var plantedPos = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            var f = feet[i];
            string side = i == 0 ? "R" : "L";   // feet[0] = right (RT), feet[1] = left (LT)
            if (f == null) { footMark[i].gameObject.SetActive(false); continue; }
            bool down = f.planted && !f.isLifting;
            float grip = f.curTractionCoef;
            bool slip = f.slipping || f.slipVel.magnitude > 0.05f;
            var col = GripColor(grip);
            if (down && slip && Time.unscaledTime % 0.3f < 0.15f) col = Bad;

            if (down)
            {
                plantedPos[planted++] = f.targetPos;
                Place(footMark[i], cam, f.targetPos, 0f);
                footImg[i].color = new Color(col.r, col.g, col.b, 0.85f);
                if (hips != null && i < hips.Length && hips[i] != null)
                {
                    var leg = hips[i].position - f.targetPos;
                    sum += leg;
                    bestLeg = Mathf.Min(bestLeg, AngleFromUp(leg));
                }
            }
            else { footMark[i].gameObject.SetActive(false); liftedPos = f.targetPos; anyLifted = true; }

            string surf = down ? Surf(f) : "LIFTED";
            sb.Append($"<color=#{Hex(down ? col : Color.white)}>{side}  {surf}</color>");
            if (down) { float slope = AngleFromUp(f.plantNormal); sb.Append($"<pos=250><color=#{Hex(Ramp(slope / SteepSlope))}>{slope:0}°</color>"); }
            sb.Append($"<pos=340><color=#{Hex(down ? col : Color.white)}>{(down ? grip.ToString("0.00", CultureInfo.InvariantCulture) : "")}</color>");
            sb.Append($"<pos=440>{Bar(f.fatigue)}");
            if (down && slip) sb.Append("  <color=#" + Hex(Bad) + ">SLIPPING</color>");
            sb.Append('\n');
        }

        // lean, as the game judges it
        sb.Append(Header("BALANCE", "", "NOW", "TO LIMIT"));
        if (planted > 0)
        {
            var c = Ramp((bestLeg - LeanFrom) / (LegHoldsAngle - LeanFrom));
            sb.Append($"<color=#{Hex(c)}>LEAN<pos=340>{bestLeg:0}°</color><pos=440>{Bar(bestLeg / LegHoldsAngle)}\n");
        }
        else sb.Append("LEAN<pos=340>-\n");

        // support: line between the planted feet, centre of mass on the ground between them
        var com = pm.centerOfMass;
        // the game's own fall timers only run when you're about to go down: off balance (down after 0.125 s), or
        // standing with your weight outside your feet (magicFallOffTimer)
        bool falling = pm.offBalanceDeathTimer > 0f || pm.magicFallOffTimer > 0f;
        float spread = 0f;
        if (planted == 2)
        {
            float legLen = pm.legLength > 0.01f ? pm.legLength : 0.79f;
            spread = (plantedPos[0] - plantedPos[1]).magnitude / legLen;
            Line(supportLine, cam, plantedPos[0], plantedPos[1]);
            float y = (plantedPos[0].y + plantedPos[1].y) * 0.5f;
            var onGround = new Vector3(com.x, y, com.z);
            Place(comMark, cam, onGround, 45f);
            float outside = DistToSegment(Flat(onGround), Flat(plantedPos[0]), Flat(plantedPos[1]));
            comImg.color = falling ? Bad : Ramp(Mathf.Max(outside / OutsideRed, LeanDanger(bestLeg), SplitsDanger(spread)));
            var lc = LineRamp(spread);
            supportImg.color = new Color(lc.r, lc.g, lc.b, Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(spread - SplitsFrom)));
        }
        else if (planted == 1)
        {
            // faint line from the planted foot to the lifted one: how far apart they'd be if you put it down there
            if (anyLifted)
            {
                float legLen = pm.legLength > 0.01f ? pm.legLength : 0.79f;
                float reach = (plantedPos[0] - liftedPos).magnitude / legLen;
                Line(supportLine, cam, plantedPos[0], liftedPos);
                var lc = LineRamp(reach);
                supportImg.color = new Color(lc.r, lc.g, lc.b, 0.35f);
            }
            else supportLine.gameObject.SetActive(false);
            Place(comMark, cam, new Vector3(com.x, plantedPos[0].y, com.z), 45f);
            comImg.color = falling ? Bad : Ramp(Mathf.Max((Flat(com) - Flat(plantedPos[0])).magnitude / OutsideRed, LeanDanger(bestLeg)));
        }
        else { supportLine.gameObject.SetActive(false); comMark.gameObject.SetActive(false); }

        if (spread >= SplitsFrom)
            sb.Append($"<color=#{Hex(LineRamp(spread))}>SPLITS<pos=340>{spread / SplitsAt * 100f:0}%</color><pos=440>{Bar(spread / SplitsAt)}\n");

        SetText(sb.ToString());
    }

    // only when it changed: setting it makes the text lay itself out again
    void SetText(string s) { if (text.text != s) text.text = s; }

    // column titles, dimmed
    static string Header(string a, string b, string c, string d) => $"<alpha=#80><size=80%>{a}<pos=250>{b}<pos=340>{c}<pos=440>{d}</size><alpha=#FF>\n";

    static float LeanDanger(float bestLeg) => bestLeg >= 180f ? 0f : (bestLeg - LeanFrom) / (LegHoldsAngle - LeanFrom);
    static float SplitsDanger(float spread) => (spread - SplitsFrom) / (SplitsRed - SplitsFrom);

    static string Surf(Il2Cpp.FootData f)
    {
        try
        {
            var s = f.lastSurfInfo;
            string a = s.surf1.value.ToString();
            return string.IsNullOrEmpty(a) || a == "None" ? "GROUND" : a.ToUpperInvariant();
        }
        catch { return "GROUND"; }
    }

    static string Bar(float v)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(v * 10f), 0, 10);
        return new string('|', n) + "<alpha=#40>" + new string('|', 10 - n) + "<alpha=#FF>";
    }

    // plain maths, the game's build may have stripped some unity helpers
    static string Hex(Color c) => $"{(int)(Mathf.Clamp01(c.r) * 255):X2}{(int)(Mathf.Clamp01(c.g) * 255):X2}{(int)(Mathf.Clamp01(c.b) * 255):X2}";

    static float AngleFromUp(Vector3 v)
    {
        float len = Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
        return len < 1e-6f ? 0f : Mathf.Acos(Mathf.Clamp(v.y / len, -1f, 1f)) * Mathf.Rad2Deg;
    }
    static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    // a marker at a world point (hidden when it's behind the camera)
    void Place(RectTransform mark, Camera cam, Vector3 world, float rot)
    {
        var sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) { mark.gameObject.SetActive(false); return; }
        mark.gameObject.SetActive(true);
        mark.position = new Vector3(sp.x, sp.y, 0f);
        mark.localEulerAngles = new Vector3(0f, 0f, rot);
    }

    void Line(RectTransform line, Camera cam, Vector3 a, Vector3 b)
    {
        var pa = cam.WorldToScreenPoint(a); var pb = cam.WorldToScreenPoint(b);
        if (pa.z <= 0f || pb.z <= 0f) { line.gameObject.SetActive(false); return; }
        line.gameObject.SetActive(true);
        var d = (Vector2)(pb - pa);
        float scale = canvas.scaleFactor;
        line.position = new Vector3((pa.x + pb.x) * 0.5f, (pa.y + pb.y) * 0.5f, 0f);
        line.sizeDelta = new Vector2(d.magnitude / scale, 3f);
        line.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    bool Make(Il2Cpp.Menu menu)
    {
        root = new GameObject("WalkingCoach");
        UnityEngine.Object.DontDestroyOnLoad(root);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        supportLine = Box("support", 3f, out supportImg);
        for (int i = 0; i < 2; i++) footMark[i] = Box("foot" + i, 26f, out footImg[i]);
        comMark = Box("weight", 16f, out comImg);

        var t = new GameObject("text");
        t.transform.SetParent(root.transform, false);
        text = t.AddComponent<TextMeshProUGUI>();
        TMP_Text src = menu != null ? menu.ContinueGameButtonText : null;
        if (src != null) { text.font = src.font; text.fontSharedMaterial = src.fontSharedMaterial; }
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(40f, 40f);
        rt.sizeDelta = new Vector2(800f, 260f);
        return true;
    }

    RectTransform Box(string name, float size, out Image img)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        img = go.AddComponent<Image>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.sizeDelta = new Vector2(size, size);
        return rt;
    }
}
