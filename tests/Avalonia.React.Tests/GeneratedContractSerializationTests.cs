using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalonia.React.Tests;

/// <summary>
/// Verifies that the shape emitted by the source generator round-trips with the bridge's
/// <see cref="JsonSerializerOptions.Web"/> settings.
/// </summary>
public sealed class GeneratedContractSerializationTests
{
    [Fact]
    public void Dto_and_enum_metadata_round_trip_with_web_options()
    {
        var profile = new GeneratedProfile
        {
            Id = 7,
            Name = "Ada",
            Status = GeneratedStatus.Active,
        };

        var json = JsonSerializer.Serialize(profile, JsonSerializerOptions.Web);

        Assert.Contains("\"user_id\":7", json);
        Assert.Contains("\"name\":\"Ada\"", json);
        Assert.Contains("\"status\":\"active\"", json);

        var roundTrip = JsonSerializer.Deserialize<GeneratedProfile>(json, JsonSerializerOptions.Web)!;
        Assert.Equal(7, roundTrip.Id);
        Assert.Equal("Ada", roundTrip.Name);
        Assert.Equal(GeneratedStatus.Active, roundTrip.Status);
        Assert.Null(roundTrip.Nickname);
    }

    [JsonConverter(typeof(JsonStringEnumConverter<GeneratedStatus>))]
    public enum GeneratedStatus
    {
        [JsonStringEnumMemberName("active")]
        Active,

        [JsonStringEnumMemberName("disabled")]
        Disabled,
    }

    public sealed class GeneratedProfile
    {
        [JsonPropertyName("user_id")]
        public double Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("nickname")]
        public string? Nickname { get; set; }

        [JsonPropertyName("status")]
        public GeneratedStatus Status { get; set; }
    }
}
