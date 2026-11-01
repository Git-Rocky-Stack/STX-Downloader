using System.Text.Json;
using System.Text.Json.Serialization;
using Stx.Audio;

namespace Stx.ViewModels;

/// <summary>Saves measured facts as JSON. Digital silence measures as minus infinity, which plain JSON can't hold.</summary>
public static class FactsJson
{
    public const string NoAudio = "{\"noAudio\":true}";

    static readonly JsonSerializerOptions Options = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static string Write(AudioFacts facts) => JsonSerializer.Serialize(facts, Options);

    public static bool IsNoAudio(string? json) => json == NoAudio;

    public static AudioFacts? Read(string? json)
    {
        if (string.IsNullOrEmpty(json) || IsNoAudio(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AudioFacts>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
