using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MonitorAgent.Infrastructure
{
    static class JsonDefaults
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };
    }
}