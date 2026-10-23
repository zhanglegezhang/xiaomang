using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;
using System.Linq;

namespace ShareX.ImageEffectsLib
{
    internal static class ImageEffectImport
    {
        internal static ImageEffectPreset Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("特效配置为空。");
            using var reader = new JsonTextReader(new StringReader(json));
            var serializer = new JsonSerializer
            {
                SerializationBinder = new ImageEffectsSerializationBinder(),
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                CheckAdditionalContent = true,
                MaxDepth = 64
            };
            serializer.Converters.Add(new StringEnumConverter());
            // Unknown properties remain compatible with older/newer preset formats.
            // Do not use JsonHelpers here: it deliberately suppresses parse errors.
            var preset = serializer.Deserialize<ImageEffectPreset>(reader);
            if (preset?.Effects == null || preset.Effects.Count == 0 || preset.Effects.Any(effect => effect == null))
                throw new InvalidDataException("特效配置必须包含至少一项有效特效。");
            return preset;
        }
    }
}
