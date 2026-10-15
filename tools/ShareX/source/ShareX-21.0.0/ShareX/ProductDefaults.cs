using Newtonsoft.Json;
using System;
using System.IO;

namespace ShareX
{
    internal static class ProductDefaults
    {
        private static readonly Lazy<string> Application = new(() => Read("ApplicationDefaults.json"));
        private static readonly Lazy<string> Hotkeys = new(() => Read("HotkeyDefaults.json"));

        // Defaults are applied by constructors. Deserialization subsequently overlays the
        // user's saved settings, so installing an update never resets existing preferences.
        internal static void Apply(ApplicationConfig settings) => Populate(Application.Value, settings);
        internal static void Apply(HotkeysConfig settings) => Populate(Hotkeys.Value, settings);

        private static void Populate(string json, object target)
        {
            JsonConvert.PopulateObject(json, target, new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                MissingMemberHandling = MissingMemberHandling.Error
            });
        }

        private static string Read(string name)
        {
            using Stream stream = typeof(ProductDefaults).Assembly.GetManifestResourceStream("ShareX.Defaults." + name)
                ?? throw new InvalidOperationException("Missing product defaults: " + name);
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
