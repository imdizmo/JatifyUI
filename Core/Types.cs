namespace Jatify.UI
{
    // Another mod's type by its full name (Outer+Nested for nested ones), or null. Not AccessTools.TypeByName: that walks every
    // type of every loaded assembly, warns for each one that can't load all of its types (VNEI without Auga filled the log, #24),
    // and warns again when the mod isn't installed. Asking each assembly for the name loads nothing else.
    internal static class Types
    {
        internal static System.Type Find(string name)
        {
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type t = null;
                try { t = a.GetType(name, false); } catch (System.Exception) { }   // a broken mod assembly: not ours to report
                if (t != null) return t;
            }
            return null;
        }
    }
}
