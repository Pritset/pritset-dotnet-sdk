using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pritset.Internal;

internal static class DocumentData
{
    internal static string Serialize(object? data)
    {
        try
        {
            if (data is string json)
            {
                JToken.Parse(json);
                return json;
            }
            return JsonConvert.SerializeObject(data, JsonDefaults.Settings);
        }
        catch (JsonException)
        {
            throw new ArgumentException("Document data must be JSON serializable.", nameof(data));
        }
    }
}
