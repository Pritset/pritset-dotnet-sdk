using Newtonsoft.Json;

namespace Pritset.Internal;

internal static class JsonDefaults
{
    internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        DateParseHandling = DateParseHandling.DateTimeOffset,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };
}
