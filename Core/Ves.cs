using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Jatify.UI
{
    // Valheim Enchantment System (kg.ValheimEnchantmentSystem), soft, by reflection: which scrolls enchant an item and how many you
    // carry, so tooltips can show the scrolls instead of its "Can be enchanted with ..." line. A no-op without it.
    internal static class Ves
    {
        internal const string Guid = "kg.ValheimEnchantmentSystem";
        private static bool _bound; private static MethodInfo _reqs, _count; private static FieldInfo _normal, _blessed, _prefab;
        private static string _phrase;

        private static bool Bind()
        {
            if (_bound) return _reqs != null;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(Guid)) return false;   // may load after us: asked again next time
            _bound = true;
            var synced = AccessTools.TypeByName("kg.ValheimEnchantmentSystem.Configs.SyncedData");
            var reqT = AccessTools.TypeByName("kg.ValheimEnchantmentSystem.Configs.SyncedData+EnchantmentReqs");
            var singleT = AccessTools.TypeByName("kg.ValheimEnchantmentSystem.Configs.SyncedData+SingleReq");
            _reqs = synced != null ? AccessTools.Method(synced, "GetReqs", new[] { typeof(string) }) : null;
            _normal = reqT != null ? AccessTools.Field(reqT, "enchant_prefab") : null; _blessed = reqT != null ? AccessTools.Field(reqT, "blessed_enchant_prefab") : null;
            _prefab = singleT != null ? AccessTools.Field(singleT, "prefab") : null;
            _count = AccessTools.Method(AccessTools.TypeByName("kg.ValheimEnchantmentSystem.EnchantmentMaterialService"), "CountAvailable");   // its own count (bag, and chests through AzuCraftyBoxes)
            if (_reqs == null || _normal == null || _blessed == null || _prefab == null) { _reqs = null; Log.Warn("Valheim Enchantment System found but its scroll data isn't where we look: scroll tiles off"); }
            return _reqs != null;
        }

        // its tooltip line ("Can be enchanted with"), localized: the line our scroll tiles replace
        internal static string Phrase
        {
            get
            {
                if (_phrase == null && Localization.instance != null) { var s = Localization.instance.Localize("$enchantment_canbeenchantedwith"); if (!s.StartsWith("[")) _phrase = s; }
                return _phrase;
            }
        }

        // the scrolls that enchant this item (normal, then blessed), as item prefabs; empty when it can't be enchanted
        internal static List<GameObject> Scrolls(ItemDrop.ItemData item) => Scrolls(item?.m_dropPrefab != null ? item.m_dropPrefab.name : item?.m_shared != null && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item.m_shared)?.name : null);
        internal static List<GameObject> Scrolls(string prefab)
        {
            var list = new List<GameObject>();
            if (string.IsNullOrEmpty(prefab) || ObjectDB.instance == null || !Bind()) return list;
            try
            {
                var reqs = _reqs.Invoke(null, new object[] { prefab });
                if (reqs == null) return list;
                foreach (var f in new[] { _normal, _blessed })
                {
                    var single = f.GetValue(reqs); var name = single != null ? _prefab.GetValue(single) as string : null;
                    var go = string.IsNullOrEmpty(name) ? null : ObjectDB.instance.GetItemPrefab(name);
                    if (go != null && go.GetComponent<ItemDrop>() != null) list.Add(go);
                }
            }
            catch (System.Exception e) { Log.Warn("Valheim Enchantment System scrolls: " + (e.InnerException?.Message ?? e.Message)); _reqs = null; }
            return list;
        }

        // its enchant window's scroll slot has no tooltip of its own: ours, for the scroll it asks for (the one you carry, else the
        // scroll itself), normal or blessed as its Use Bless box says. Nothing while no item is in the window
        internal static void ScrollTip(GameObject tooltipPrefab, Ledger l)
        {
            if (tooltipPrefab == null || !Bind()) return;
            var ui = AccessTools.TypeByName("kg.ValheimEnchantmentSystem.UI.VES_UI");
            var view = AccessTools.Field(ui, "_view")?.GetValue(null) as Component; var ctl = AccessTools.Field(ui, "_controller")?.GetValue(null);
            var slot = view != null ? AccessTools.Field(view.GetType(), "ScrollRoot")?.GetValue(view) as Transform : null;
            if (slot == null || ctl == null || slot.GetComponent<UITooltip>() != null) return;
            var cur = AccessTools.Field(ctl.GetType(), "_currentItem"); var bless = AccessTools.Field(ctl.GetType(), "_useBless");
            if (cur == null || bless == null) return;
            var tip = slot.gameObject.AddComponent<UITooltip>(); tip.m_tooltipPrefab = tooltipPrefab;
            var sync = slot.gameObject.AddComponent<ScrollTipSync>();
            sync.Tip = tip; sync.Current = () =>
            {
                var list = Scrolls(cur.GetValue(ctl) as ItemDrop.ItemData); if (list.Count == 0) return null;
                var want = list[(bool)bless.GetValue(ctl) && list.Count > 1 ? 1 : 0];
                var p = Player.m_localPlayer;
                if (p != null) foreach (var it in p.GetInventory().GetAllItems()) if (it.m_dropPrefab != null && it.m_dropPrefab.name == want.name) return it;
                return want.GetComponent<ItemDrop>().m_itemData;
            };
            ItemTip.For(tip, () => sync.Item);
            l.Do(() => { }, () => { if (sync != null) Object.Destroy(sync); if (tip != null) Object.Destroy(tip); var src = slot != null ? slot.GetComponent<ItemTip.Source>() : null; if (src != null) Object.Destroy(src); });
        }

        // Valheim opens a tooltip only when it has text, and ours draws over the one it opens: the slot's tooltip is on, titled
        // with the scroll, only while the window asks for one (as the mod does with its own two)
        internal sealed class ScrollTipSync : MonoBehaviour
        {
            internal UITooltip Tip; internal System.Func<ItemDrop.ItemData> Current; internal ItemDrop.ItemData Item;
            private void Update()
            {
                if (Tip == null || Current == null) return;
                Item = Current();
                if (Tip.enabled != (Item != null)) Tip.enabled = Item != null;
                if (Item != null) { var n = Item.m_shared.m_name; if (Tip.m_topic != n) Tip.m_topic = n; }
            }
        }

        // how many you have: its own count when it has one (what its panel says), else the bag
        internal static int Count(GameObject scroll)
        {
            var p = Player.m_localPlayer; if (p == null || scroll == null) return 0;
            if (_count != null) { try { return (int)_count.Invoke(null, new object[] { p, scroll.name }); } catch { _count = null; } }
            int n = 0; foreach (var it in p.GetInventory().GetAllItems()) if (it.m_dropPrefab != null && it.m_dropPrefab.name == scroll.name) n += it.m_stack;
            return n;
        }
    }
}
