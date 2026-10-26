using HarmonyLib;
using TMPro;
using UnityEngine;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // The server password prompt (shown while connecting) as the Jatify design's "Enter password" window. Valheim's own
    // dialog stays open but invisible, so everything that waits on it keeps working, and the password goes through
    // Valheim's own OnPasswordEntered.
    internal static class PasswordPrompt
    {
        private static RectTransform _root;
        private static Window _win;
        private static TMP_InputField _field;
        private static OnOff _remember;
        private static TextMeshProUGUI _host;

        internal static void Show(ZNet znet)
        {
            var group = znet.m_passwordDialog.GetComponent<CanvasGroup>() ?? znet.m_passwordDialog.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
            if (_root == null) Build();
            Set(_host, $"{Passwords.PendingHost ?? "The server"} asks for a password.");
            _remember.SetEnabled(true);
            _remember.Set(Passwords.Available && Module.RememberPasswords.Value);
            _remember.SetEnabled(Passwords.Available);
            _field.text = "";
            _win.Show(true);
            Focus(_field);
        }

        private static void Build()
        {
            // Loading-screen mods (Intermission) draw their art on a very high canvas; stay above them.
            _root = MakeCanvas("CampfirePasswordCanvas", 32000);
            _win = new Window(_root, "password", 480f, Window.Top + 128f + Window.Bottom, Cancel);
            _win.SetTitle("Enter password");
            _win.Buttons("Cancel", "Join", Submit);
            float w = _win.BodyW;
            _host = Para(_win.Body, "", "medium", 13f, C("mute"), 0f, 0f, w, 22f);
            _field = LabelledField(_win.Body, "Password", "Password", 32f, w, "lock", Submit, password: true);
            _remember = SwitchRow(_win.Body, "Remember on this PC", 0f, 106f, false, on => Passwords.RememberThis = on, 260f);
        }

        private static void Submit()
        {
            var pwd = _field.text ?? "";
            if (pwd.Length == 0 || !_win.Shown) return;
            // The server may already have dropped us (wrong password, timeout): nothing to answer then.
            var rpc = ZNet.instance != null ? Traverse.Create(ZNet.instance).Field("m_tempPasswordRPC").GetValue<ZRpc>() : null;
            if (rpc == null || !rpc.IsConnected()) { _win.Show(false); return; }
            Passwords.RememberThis = _remember.Value;
            _win.Show(false);   // gone this frame, not faded: the loading bar carries on from here
            if (ZNet.instance != null) Traverse.Create(ZNet.instance).Method("OnPasswordEntered", new[] { typeof(string) }).GetValue(pwd);
        }

        // Valheim's prompt has no way out; this one logs out without saving the character.
        private static void Cancel()
        {
            _win.Show(false);
            Game.instance?.Logout(save: false);
        }

        internal static void Unload()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.root.gameObject);
            _root = null; _win = null;
        }

        internal static void Tick()
        {
            if (_win == null || _root == null) { _win = null; _root = null; return; }
            if (!_win.Shown) return;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }
    }

    [HarmonyPatch(typeof(ZNet), "RPC_ClientHandshake")]
    internal static class OurPasswordPrompt
    {
        // Valheim opened its prompt and no saved password answered it: show ours over it.
        private static void Postfix(ZNet __instance, bool needPassword)
        {
            if (!needPassword) return;
            if (__instance.m_passwordDialog.gameObject.activeSelf) PasswordPrompt.Show(__instance);
        }
    }
}
