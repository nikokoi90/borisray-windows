using System.Text;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace BorisRay.Core;

// Event parsing avoids object construction and type activation. Anchors can refer
// only to completed values; expansion is bounded before cloning, so aliases cannot
// create cycles or an unbounded expansion in the elevated client.
internal static class BoundedYaml
{
    public static JsonObject Parse(string text)
    {
        var parser = new Parser(new StringReader(text));
        var anchors = new Dictionary<string, JsonNode>(); var count = 0; var expansion = 0;
        parser.Consume<StreamStart>(); parser.Consume<DocumentStart>();
        var root = Read(0) as JsonObject ?? throw new ArgumentException("Clash YAML должен содержать объект с proxies.");
        parser.Consume<DocumentEnd>(); parser.Consume<StreamEnd>(); return root;

        JsonNode Read(int depth)
        {
            if (depth > 32 || ++count > 16_384) throw new ArgumentException("Clash YAML слишком сложный.");
            if (parser.TryConsume<AnchorAlias>(out var alias))
            {
                if (!anchors.TryGetValue(alias.Value.Value, out var saved)) throw new ArgumentException("Clash YAML содержит циклическую или неизвестную ссылку.");
                expansion += Encoding.UTF8.GetByteCount(saved.ToJsonString());
                if (expansion > Profiles.MaximumBytes) throw new ArgumentException("Clash YAML слишком большой после раскрытия шаблонов.");
                return saved.DeepClone();
            }
            var start = parser.Current as NodeEvent ?? throw new ArgumentException("Некорректный Clash YAML.");
            JsonNode result;
            if (parser.TryConsume<Scalar>(out var scalar)) result = JsonValue.Create(scalar.Value)!;
            else if (parser.TryConsume<SequenceStart>(out _))
            {
                var list = new JsonArray(); while (!parser.Accept<SequenceEnd>(out _)) list.Add(Read(depth + 1)); parser.Consume<SequenceEnd>(); result = list;
            }
            else
            {
                parser.Consume<MappingStart>(); var map = new JsonObject(); var explicitKeys = new HashSet<string>();
                while (!parser.Accept<MappingEnd>(out _))
                {
                    var key = parser.Consume<Scalar>().Value; var value = Read(depth + 1);
                    if (key == "<<")
                    {
                        var merges = value is JsonArray a ? a.ToArray() : [value];
                        foreach (var merge in merges)
                        {
                            if (merge is not JsonObject m) throw new ArgumentException("Некорректный YAML-шаблон.");
                            foreach (var pair in m) if (!map.ContainsKey(pair.Key)) map[pair.Key] = pair.Value?.DeepClone();
                        }
                    }
                    else { if (!explicitKeys.Add(key)) throw new ArgumentException("Повторяющийся ключ Clash YAML."); map[key] = value; }
                }
                parser.Consume<MappingEnd>(); result = map;
            }
            if (!start.Anchor.IsEmpty)
            {
                if (anchors.Count >= 256 || anchors.ContainsKey(start.Anchor.Value)) throw new ArgumentException("Слишком много или повторяющиеся YAML-шаблоны.");
                anchors.Add(start.Anchor.Value, result);
            }
            return result;
        }
    }
}
