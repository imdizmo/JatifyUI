using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Jatify.Campfire
{
    // Remembered server passwords, kept in Valheim's own preferences (PlatformPrefs) and encrypted with
    // AES-256. The key is derived from this PC's machine id and your Steam ID, so a copied prefs file
    // (backup, screenshot, shared profile) can't be read elsewhere. It is not a vault: another program
    // running as you on this PC could derive the same key. A password is saved only after it got you
    // in, and forgotten when the server turns it down.
    internal static class Passwords
    {
        private const string Service = "campfire-valheim";
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private static string _pending, _typed;
        private static bool _autoFilled;
        internal static bool RememberThis;
        internal static string PendingHost => _pending;

        internal static bool Available => true;
        internal static bool Has(string host) => Get(host) != null;

        // Only plain host:port addresses reach the keyring command line or the password file.
        private static bool Ok(string host) => !string.IsNullOrEmpty(host) && host.Length < 200 && host.All(c => char.IsLetterOrDigit(c) || ".-:[]_".IndexOf(c) >= 0);

        // Forget any half-finished join: nothing pending, and Valheim holds no password to send.
        // Called whenever the character screen opens and before hosting a world, so a password can
        // never be saved under, or sent to, a server other than the one it was typed for.
        internal static void Reset()
        {
            _pending = null; _typed = null; _autoFilled = false;
            Traverse.Create(typeof(FejdStartup)).Property("ServerPassword").SetValue(null);
        }

        internal static string Get(string host)
        {
            if (!Available || !Ok(host)) return null;
            if (_cache.TryGetValue(host, out var hit)) return hit;
            string pw = null;
            try { pw = Load(host); }
            catch (Exception e) { Log.Warn("password lookup failed: " + e.Message); }
            if (string.IsNullOrEmpty(pw)) pw = null;
            _cache[host] = pw;
            return pw;
        }

        internal static void Set(string host, string pw)
        {
            if (!Available || !Ok(host)) return;
            try { Store(host, pw); _cache[host] = pw; Log.Info($"password saved for {host}"); }
            catch (Exception e) { Log.Warn("password save failed: " + e.Message); }
        }

        internal static void Forget(string host)
        {
            if (!Available || !Ok(host)) return;
            try { Store(host, null); _cache[host] = null; Log.Info($"password forgotten for {host}"); }
            catch (Exception e) { Log.Warn("password forget failed: " + e.Message); }
        }

        // Before joining: hand Valheim the saved password; its own handshake uses it instead of asking.
        internal static void BeforeJoin(string host)
        {
            Reset();
            if (!Ok(host)) return;
            _pending = host;
            RememberThis = Module.RememberPasswords.Value;
            Log.Info($"joining {host}: saved password {(_autoFilled ? "used" : "none")}");
            var pw = Get(host);
            _autoFilled = pw != null;
            Traverse.Create(typeof(FejdStartup)).Property("ServerPassword").SetValue(pw);
            _ours = true;
        }

        // Any join we didn't start (Valheim's own screen, an invite) after one of ours was abandoned: drop what that one
        // left, so its saved password never reaches another server and a password typed there isn't saved under its name
        private static bool _ours;
        internal static void AnyJoin()
        {
            if (!_ours && _pending != null) { Log.Info($"a join Campfire didn't start: forgetting the pending {_pending}"); Reset(); }
            _ours = false;
        }

        internal static void Typed(string pwd) { if (_pending != null) _typed = pwd; Log.Info($"password entered for {_pending ?? "(no pending join)"}"); }

        // In the world: the password worked, keep it (when the player wants that).
        internal static void Tick()
        {
            // In the world as a client, spawned: the password got us in.
            if (_pending == null || Player.m_localPlayer == null || ZNet.instance == null || ZNet.instance.IsServer()) return;
            Log.Info($"in {_pending}: {(_typed == null ? "no password typed" : RememberThis ? "remembering the password" : "not remembering (switch off)")}");
            if (_typed != null && RememberThis) Set(_pending, _typed);
            _pending = null; _typed = null;
            Traverse.Create(typeof(FejdStartup)).Property("ServerPassword").SetValue(null);
        }

        // Back at the menu with "wrong password": drop the saved one so Valheim asks next time.
        internal static void ConnectFailed(ZNet.ConnectionStatus status)
        {
            if (_pending == null) return;
            Log.Info($"connection to {_pending} ended: {status}");
            if (status == ZNet.ConnectionStatus.ErrorPassword && _autoFilled)
            {
                Forget(_pending);
                CharScreen.Toast("The saved password was wrong and has been forgotten");
            }
            _pending = null; _typed = null;
            Traverse.Create(typeof(FejdStartup)).Property("ServerPassword").SetValue(null);
        }

        // ---- storage: Valheim's prefs, AES-256-CBC, random IV per write ---------------------------

        private static string PrefKey(string host) => "campfire.pw." + host;

        private static string Load(string host)
        {
            var blob = PlatformPrefs.GetString(PrefKey(host), "");
            if (string.IsNullOrEmpty(blob)) return null;
            var data = Convert.FromBase64String(blob);
            using (var aes = Aes.Create())
            {
                aes.Key = Key();
                aes.IV = data.Take(16).ToArray();
                using (var dec = aes.CreateDecryptor())
                    return Encoding.UTF8.GetString(dec.TransformFinalBlock(data, 16, data.Length - 16));
            }
        }

        private static void Store(string host, string pw)
        {
            if (pw == null) { PlatformPrefs.SetString(PrefKey(host), ""); return; }
            using (var aes = Aes.Create())
            {
                aes.Key = Key();
                aes.GenerateIV();
                using (var enc = aes.CreateEncryptor())
                {
                    var plain = Encoding.UTF8.GetBytes(pw);
                    var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                    PlatformPrefs.SetString(PrefKey(host), Convert.ToBase64String(aes.IV.Concat(cipher).ToArray()));
                }
            }
        }

        private static byte[] _key;
        private static byte[] Key()
        {
            if (_key != null) return _key;
            string machine = Environment.MachineName;
            try { foreach (var f in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" }) if (File.Exists(f)) { machine = File.ReadAllText(f).Trim(); break; } } catch { }
            string steam = "";
            try { steam = Steamworks.SteamUser.GetSteamID().m_SteamID.ToString(); } catch { }
            // No Steam ID yet: don't cache a key without it, or this run's saves won't open next run.
            if (steam.Length == 0 || steam == "0") throw new InvalidOperationException("Steam is not ready");
            using (var sha = SHA256.Create())
                return _key = sha.ComputeHash(Encoding.UTF8.GetBytes("campfire-valheim|" + machine + "|" + steam));
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.JoinServer))]
    internal static class JoinGuard
    {
        private static void Prefix() => Passwords.AnyJoin();
    }

    [HarmonyPatch(typeof(ZNet), "OnPasswordEntered")]
    internal static class CapturePassword
    {
        private static void Prefix(string pwd) => Passwords.Typed(pwd);
    }

    [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
    internal static class PasswordRejected
    {
        private static void Prefix(ZNet.ConnectionStatus statusOverride) =>
            Passwords.ConnectFailed(statusOverride == ZNet.ConnectionStatus.None ? ZNet.GetConnectionStatus() : statusOverride);
    }
}
