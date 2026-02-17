using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace MinecraftLaunch.Base.Models.SHA1;


[JsonConverter(typeof(Sha1DataJsonConverter))]
[InlineArray(20)]
public partial struct Sha1Data : IEquatable<Sha1Data>
{
    public bool Equals(Sha1Data other) => _data == other._data;

    public override bool Equals(object obj) => obj is Sha1Data other && Equals(other);

    public override int GetHashCode() => _data.GetHashCode();

    private byte _data;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FormatTo(Span<char> destination)
    {
        for (var i = 0; i < 20; i++)
        {
            var b = this[i];
            destination[i * 2] = ToHexCharLower(b >> 4);
            destination[i * 2 + 1] = ToHexCharLower(b & 0x0F);
        }
        return;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static char ToHexCharLower(int b) => b switch
        {
            < 10 => (char)('0' + b),
            _ => (char)('a' + (b - 10))
        };
    }
    /// <summary>
    /// 返回 40 位小写十六进制字符串。
    /// </summary>
    public override string ToString()
    {
        // 栈上分配 40 个 char，避免托管内存分配
        Span<char> buffer = stackalloc char[40];
        FormatTo(buffer);
        return new string(buffer);
    }

  
    
    public sealed class Sha1DataJsonConverter : JsonConverter<Sha1Data>
{
    public override Sha1Data Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert != typeof(Sha1Data))
        {
            ThrowHelper.ThrowInvalidTargetType();
        }
        if (reader.TokenType != JsonTokenType.String)
            ThrowHelper.ThrowUnexpectedTokenType();
        
        var utf8Hex = reader.ValueSpan;
        if (utf8Hex.Length != 40)
            ThrowHelper.ThrowInvalidLength(utf8Hex.Length);
        Unsafe.SkipInit(out Sha1Data result);
        for (var i = 0; i < 20; i++)
        {
            var idx = i * 2;
            result[i] = ParseHex(utf8Hex[idx], utf8Hex[idx + 1]);
        }
        return result;
    }
    

    public override void Write(Utf8JsonWriter writer, Sha1Data value, JsonSerializerOptions options)
    {
        var buffer = (Span<byte>)stackalloc byte[40];
        for (var i = 0; i < 20; i++)
        {
            var b = value[i];
            buffer[i * 2] = ToHexCharLower(b >> 4);
            buffer[i * 2 + 1] = ToHexCharLower(b & 0x0F);
        }
        writer.WriteStringValue(buffer);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)] 
    private static byte ParseHex(byte c1, byte c2)
    {
        var h = (c1 & 0x0F) + ((c1 & 0x40) != 0 ? 9 : 0);
        var l = (c2 & 0x0F) + ((c2 & 0x40) != 0 ? 9 : 0);
        return (byte)((h << 4) | l);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToHexCharLower(int b) => b switch
    {
        < 10 => (byte)('0' + b),
        _ => (byte)('a' + (b - 10))
    };
    
    private static class ThrowHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowUnexpectedTokenType() => throw new JsonException("Unexpected token type for SHA1 data, expected a string.");
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidLength(int len) => throw new JsonException($"SHA1 hex string must be 40 characters long, got {len}.");
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidTargetType()
        {
            throw new JsonException("Invalid target type.");
        }
    }
}
    
    [JsonSerializable(typeof(Sha1Data))]
    [JsonSerializable(typeof(Sha1Data[]))]
    public sealed partial class Sha1DataSerializerContext : JsonSerializerContext;
}

