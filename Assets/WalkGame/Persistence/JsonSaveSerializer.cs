using System;
using Newtonsoft.Json;
using WalkGame.Core;

namespace WalkGame.Persistence
{
    /// <summary>
    /// Human-readable JSON serializer for prototype (DATA_MODEL.md 23).
    /// Settings deliberately conservative: no type-name handling, UTC ISO dates,
    /// indented output so saves stay debuggable.
    /// </summary>
    public sealed class JsonSaveSerializer : ISaveSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                // Fields are the canonical shape in this codebase; serialize them as-is.
                IgnoreSerializableAttribute = true,
            },
        };

        public string Serialize(PlayerProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            return JsonConvert.SerializeObject(profile, Settings);
        }

        public PlayerProfile Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<PlayerProfile>(payload, Settings);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
