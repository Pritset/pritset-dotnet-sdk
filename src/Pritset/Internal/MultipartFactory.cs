using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Pritset.Internal;

internal static class MultipartFactory
{
    internal static MultipartFormDataContent Create(params (string Name, string Value)[] fields)
    {
        var content = new MultipartFormDataContent();
        foreach ((string name, string value) in fields)
        {
            content.Add(new StringContent(value, Encoding.UTF8), name);
        }
        return content;
    }

    internal static void AddUpload(MultipartFormDataContent content, string fieldName, Upload upload)
    {
        var fileContent = new StreamContent(new NonDisposingStream(upload.Stream));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType ?? "application/octet-stream");
        content.Add(fileContent, fieldName, upload.FileName);
    }
}
