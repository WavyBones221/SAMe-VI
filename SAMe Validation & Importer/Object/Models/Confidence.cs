using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAMe_VI.Object.Models
{
    [JsonConverter(typeof(ConfidenceValueConverterFactory))]
    internal sealed class ConfidenceValue<T>
    {
        public T? Value { get; init; }
        public double? Confidence { get; init; }
        public bool? userValidated { get; init; }
        public override string ToString() => $"{Value} (confidence: {Confidence?.ToString("0.###") ?? "n/a"})";
    }

    internal sealed class ConfidenceValueConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ConfidenceValue<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type? tArg = typeToConvert.GetGenericArguments()[0];
            Type? converterType = typeof(ConfidenceValueConverter<>).MakeGenericType(tArg);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }

        private sealed class ConfidenceValueConverter<T> : JsonConverter<ConfidenceValue<T>>
        {
            public override ConfidenceValue<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException($"An object was expected and found {reader.TokenType}.");
                }
                using JsonDocument doc = JsonDocument.ParseValue(ref reader);
                JsonElement root = doc.RootElement;

                double? confidence = null;
                if (root.TryGetProperty("confidence", out JsonElement confProp) && confProp.ValueKind == JsonValueKind.Number)
                {
                    if (confProp.TryGetDouble(out double c))
                    {
                        confidence = c;
                    }
                }

                bool? userValidated = null;
                if (root.TryGetProperty("userValidated", out JsonElement uvProp) && (uvProp.ValueKind == JsonValueKind.True || uvProp.ValueKind == JsonValueKind.False))
                {
                    bool b = uvProp.GetBoolean();
                    userValidated = b;
                }

                T? value = default;

                if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(List<>))
                {
                    if (root.TryGetProperty("valueArray", out JsonElement arrElem) && arrElem.ValueKind == JsonValueKind.Array)
                    {
                        Type? itemType = typeof(T).GetGenericArguments()[0];
                        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
                        foreach (JsonElement item in arrElem.EnumerateArray())
                        {
                            JsonElement payload = item;
                            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("valueObject", out JsonElement vo))
                            {
                                payload = vo;
                            }
                            object? deserialized = payload.Deserialize(itemType, options);
                            list.Add(deserialized!);
                        }
                        value = (T)list;
                    }
                }
                else if (root.TryGetProperty("valueObject", out JsonElement objElem) && objElem.ValueKind == JsonValueKind.Object)
                {
                    value = objElem.Deserialize<T>(options);
                }
                else
                {
                    Type targetType = typeof(T);

                    switch (targetType)
                    {
                        case Type t when t == typeof(string):
                            {
                                if (root.TryGetProperty("valueString", out JsonElement s) && s.ValueKind == JsonValueKind.String)
                                {
                                    value = (T)(object)s.GetString()!;
                                }
                                break;
                            }

                        case Type t when t == typeof(DateTime) || t == typeof(DateTime?):
                            {
                                if (root.TryGetProperty("valueDate", out JsonElement d) && d.ValueKind == JsonValueKind.String)
                                {
                                    if (DateTime.TryParse(d.GetString(), out DateTime dt))
                                    {
                                        value = (T)(object)dt;
                                    }
                                }
                                break;
                            }

                        case Type t when t == typeof(decimal) || t == typeof(decimal?):
                            {
                                if (root.TryGetProperty("valueNumber", out JsonElement n) && n.ValueKind == JsonValueKind.Number)
                                {
                                    if (n.TryGetDecimal(out decimal dec))
                                    {
                                        value = (T)(object)dec;
                                    }
                                }
                                break;
                            }

                        case Type t when t == typeof(double) || t == typeof(double?):
                            {
                                if (root.TryGetProperty("valueNumber", out JsonElement n) && n.ValueKind == JsonValueKind.Number)
                                {
                                    if (n.TryGetDouble(out double dbl))
                                    {
                                        value = (T)(object)dbl;
                                    }
                                }
                                break;
                            }

                        case Type t when t == typeof(int) || t == typeof(int?):
                            {
                                if (root.TryGetProperty("valueNumber", out JsonElement n) && n.ValueKind == JsonValueKind.Number)
                                {
                                    if (n.TryGetInt32(out int i))
                                    {
                                        value = (T)(object)i;
                                    }
                                    else
                                    {
                                        if (n.TryGetDouble(out double dbl))
                                        {
                                            value = (T)(object)(int)Math.Round(dbl);
                                        }
                                    }
                                }
                                break;
                            }

                        case Type t when t == typeof(bool) || t == typeof(bool?):
                            {
                                if (root.TryGetProperty("valueBool", out JsonElement b))
                                {
                                    if (b.ValueKind == JsonValueKind.True || b.ValueKind == JsonValueKind.False)
                                    {
                                        bool boolValue = b.GetBoolean();
                                        value = (T)(object)boolValue;
                                    }
                                    else if (b.ValueKind == JsonValueKind.String)
                                    {
                                        string stringValue = b.GetString()!;
                                        if (bool.TryParse(stringValue, out bool boolValue))
                                        {
                                            value = (T)(object)boolValue;
                                        }
                                    }
                                }
                                break;
                            }

                        default:
                            {
                                foreach (JsonProperty prop in root.EnumerateObject())
                                {
                                    if (prop.Name.StartsWith("value", StringComparison.OrdinalIgnoreCase))
                                    {
                                        value = prop.Value.Deserialize<T>(options);
                                        break;
                                    }
                                }
                                break;
                            }
                    }
                }

                return new ConfidenceValue<T> { Value = value, Confidence = confidence, userValidated = userValidated };
            }

            public override void Write(Utf8JsonWriter writer, ConfidenceValue<T> value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                if (value.Value is not null)
                {
                    if (value.Value is string s)
                    {
                        writer.WriteString("valueString", s);
                    }
                    else if (value.Value is DateTime dt)
                    {
                        writer.WriteString("valueDate", dt);
                    }
                    else if (value.Value is IEnumerable && value.Value is not string)
                    {
                        writer.WritePropertyName("valueArray");
                        JsonSerializer.Serialize(writer, value.Value, options);
                    }
                    else if (value.Value is bool b)
                    {
                        writer.WriteBoolean("valueBool", b);
                    }
                    else
                    {
                        writer.WritePropertyName("valueObject");
                        JsonSerializer.Serialize(writer, value.Value, options);
                    }
                }
                if (value.Confidence.HasValue)
                {
                    writer.WriteNumber("confidence", value.Confidence.Value);
                }
                if (value.userValidated.HasValue)
                {
                    writer.WriteBoolean("userValidated", value.userValidated.Value);
                }
                writer.WriteEndObject();
            }
        }
    }
}