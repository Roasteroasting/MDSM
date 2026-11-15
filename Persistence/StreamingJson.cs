using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MDSM.Inventories;

namespace MDSM.Persistence;

// JSON is used only for small extensible state records, never bulk blocks/light.
internal static class StreamingJson
{
    internal static readonly JsonSerializerOptions Options=Create();
    private static JsonSerializerOptions Create()
    {
        var options=new JsonSerializerOptions{IncludeFields=true,IgnoreReadOnlyProperties=true,MaxDepth=32};
        options.Converters.Add(new StackConverter());return options;
    }
    internal static byte[] Encode<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Options);
    internal static T Decode<T>(byte[] bytes)
    {
        try{return JsonSerializer.Deserialize<T>(bytes,Options)??throw new InvalidDataException("Missing streamed state.");}
        catch(Exception e)when(e is JsonException or ArgumentException or OverflowException){throw new InvalidDataException("Invalid streamed state.",e);}
    }
    private sealed class StackConverter:JsonConverter<ItemStack>
    {
        public override ItemStack Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {
            using var doc=JsonDocument.ParseValue(ref reader);var p=doc.RootElement;
            int count=p.GetProperty("count").GetInt32();if(count==0)return default;
            return new(new ItemId(p.GetProperty("item").GetUInt16()),count,p.GetProperty("durability").GetInt32(),p.GetProperty("instance").GetGuid());
        }
        public override void Write(Utf8JsonWriter writer,ItemStack value,JsonSerializerOptions options)
        {writer.WriteStartObject();writer.WriteNumber("item",value.Item.Value);writer.WriteNumber("count",value.Count);writer.WriteNumber("durability",value.Durability);writer.WriteString("instance",value.InstanceId);writer.WriteEndObject();}
    }
}
