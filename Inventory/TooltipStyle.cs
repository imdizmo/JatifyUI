using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Jatify.UI
{
    // Valheim makes a tooltip object on each hover and destroys it when hidden, so it is restyled as
    // it appears (glass background, our fonts) and needs no undo; unloading just hides the current one.
    [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.OnHoverStart))]
    [HarmonyAfter(Epic.Guid)]   // EpicLoot moves the text into a scroll view and its background onto that view's Content first
    internal static class TooltipStyle
    {
        private static readonly AccessTools.FieldRef<GameObject> Current = AccessTools.StaticFieldRefAccess<GameObject>(AccessTools.Field(typeof(UITooltip), "m_tooltip"));
        internal static ThemeData T;
        internal static GameObject Tip => Current();

        private static readonly AccessTools.FieldRef<UITooltip, float> Timer = AccessTools.FieldRefAccess<UITooltip, float>("m_showTimer");

        private static void Postfix(UITooltip __instance)
        {
            if (__instance != null) Timer(__instance) = 0.35f;   // Valheim waits 0.5 s (a constant) before showing it: ours show after 0.15 s (user: slow)
            var tip = Current();
            if (tip == null || T == null) return;
            if (tip.transform.Find("jatify") != null) { Divider(tip); return; }
            Kit.Node(tip.transform, "jatify").gameObject.SetActive(false); // styled marker
            var box = Utils.FindChild(tip.transform, "Scroll View") != null ? Utils.FindChild(tip.transform, "Content")?.GetComponent<Image>() : null;   // EpicLoot's: the card is its Content (the old Bkg goes at frame end)
            bool first = true;
            foreach (var img in tip.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || img.GetComponent<Mask>() != null) continue;
                if (box != null && img != box) { if (img.transform.parent == tip.transform && img.name == "Bkg") img.color = Color.clear; continue; }
                if (first)
                {   // the card style: glass and its 1 px outline (an outline image under it, inset fill on top)
                    img.sprite = Kit.Rounded(Card.R); img.type = Image.Type.Sliced; img.color = Theme.C(T.glass); first = false;
                    var line = Kit.Img(img.transform, "jatify-line", Kit.Ring(Card.R), Theme.C(T.glassLine), false); line.type = Image.Type.Sliced; line.raycastTarget = false; line.rectTransform.Fill(); line.transform.SetAsFirstSibling();
                    line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // the background lays its children out in a column: the outline stays out of it
                }
                else if (img.sprite.name.Contains("bkg") || img.sprite.name.Contains("background") || img.sprite.name.Contains("panel")) { img.color = new Color(1f, 1f, 1f, 0f); }
            }
            foreach (var t in tip.GetComponentsInChildren<TMP_Text>(true))
            {
                bool topic = t.name == "Topic";
                t.font = topic ? Fonts.Bold : Fonts.Medium;
                t.fontSize = topic ? Card.Title + 1f : Card.Body; t.enableAutoSizing = false;
                t.color = topic ? Theme.C(T.text) : Theme.C(T.dim);
            }
            Divider(tip);
        }

        // design .tip .kdiv: a short knotted divider between the name and the stats, while there is a name and Knotwork is on
        private static void Divider(GameObject tip)
        {
            var topic = FindTopic(tip.transform);
            if (topic == null) return;
            var parent = topic.transform.parent;
            var div = parent.Find("jatify-kdiv");
            if (div == null)
            {
                var d = Knots.Loose(parent, 240f, false, Theme.C(T.accent));
                var le = d.gameObject.AddComponent<LayoutElement>(); le.minHeight = le.preferredHeight = Knots.Trinity + 2f; le.preferredWidth = 240f;
                d.SetSiblingIndex(topic.transform.GetSiblingIndex() + 1);
                div = d;
            }
            bool show = Knots.On && !string.IsNullOrEmpty(topic.text);
            if (div.gameObject.activeSelf != show) div.gameObject.SetActive(show);
        }
        private static TMP_Text FindTopic(Transform tip)
        {
            var t = Utils.FindChild(tip, "Topic");   // vanilla's lookup, no allocation
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        internal static void Unload() => UITooltip.HideTooltip();
    }
}

namespace Jatify.UI
{
    // Item tooltips are ItemTip's card now (its key hints follow TooltipKeys). Valheim rebuilds the hovered slot's tooltip text
    // every frame (GetTooltip, a new string each time); while the item hasn't changed it keeps last frame's text (a second's
    // refresh for lines other mods change over time).
    [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
    internal static class TooltipKeys
    {
        internal static BepInEx.Configuration.ConfigEntry<bool> On;
        private static UITooltip _tip; private static ItemDrop.ItemData _item; private static int _stack, _dur, _q; private static bool _eq; private static float _at;

        private static bool Prefix(ItemDrop.ItemData item, UITooltip tooltip)
        {
            if (item == null || tooltip == null) return true;
            int dur = Mathf.RoundToInt(item.m_durability);
            if (tooltip == _tip && item == _item && item.m_stack == _stack && dur == _dur && item.m_quality == _q && item.m_equipped == _eq && tooltip.m_text.Length > 0 && Time.unscaledTime - _at < 1f) return false;
            _tip = tooltip; _item = item; _stack = item.m_stack; _dur = dur; _q = item.m_quality; _eq = item.m_equipped; _at = Time.unscaledTime;
            return true;
        }
    }
}
