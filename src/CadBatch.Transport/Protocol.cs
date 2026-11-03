using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CadBatch.Transport
{
    [DataContract] public sealed class BridgeEndpoint
    {
        [DataMember] public int Protocol {get;set;}=1;
        [DataMember] public string Version {get;set;}="";
        [DataMember] public int ProcessId {get;set;}
        [DataMember] public string Pipe {get;set;}="";
        [DataMember] public string Token {get;set;}="";
        [DataMember] public string Description {get;set;}="";
    }
    [DataContract] public sealed class BridgeMessage
    {
        [DataMember] public string Id {get;set;}="";
        [DataMember] public string Token {get;set;}="";
        [DataMember] public string Method {get;set;}="";
        [DataMember] public string Kind {get;set;}="request";
        [DataMember] public string Body {get;set;}="null";
    }
    [DataContract] public sealed class BridgeInfo
    {
        [DataMember] public int Protocol {get;set;}=1;
        [DataMember] public string Version {get;set;}="";
        [DataMember] public string Description {get;set;}="";
        [DataMember] public bool CanPlot {get;set;}
    }
    public static class WireJson
    {
        private static DataContractJsonSerializer Serializer(Type type)=>new DataContractJsonSerializer(type,new DataContractJsonSerializerSettings{UseSimpleDictionaryFormat=true,MaxItemsInObjectGraph=2000000});
        public static string Write<T>(T value){using(var stream=new MemoryStream()){Serializer(typeof(T)).WriteObject(stream,value);return Encoding.UTF8.GetString(stream.ToArray());}}
        public static T Read<T>(string json){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json)))return (T)Serializer(typeof(T)).ReadObject(stream)!;}
    }
    public static class BridgeProtocol
    {
        public const int Version=1;
        public const int MaximumMessageBytes=64*1024*1024;
        public static async Task WriteAsync(Stream stream,BridgeMessage message,CancellationToken token)
        {
            byte[] body=Encoding.UTF8.GetBytes(WireJson.Write(message));if(body.Length>MaximumMessageBytes)throw new IOException("Слишком большое сообщение CAD Batch.");
            byte[] prefix=BitConverter.GetBytes(body.Length);await stream.WriteAsync(prefix,0,prefix.Length,token).ConfigureAwait(false);await stream.WriteAsync(body,0,body.Length,token).ConfigureAwait(false);await stream.FlushAsync(token).ConfigureAwait(false);
        }
        private static async Task ReadExactly(Stream stream,byte[] data,CancellationToken token)
        {
            int offset=0;while(offset<data.Length){int count=await stream.ReadAsync(data,offset,data.Length-offset,token).ConfigureAwait(false);if(count==0)throw new EndOfStreamException("Соединение с AutoCAD закрыто.");offset+=count;}
        }
        public static async Task<BridgeMessage> ReadAsync(Stream stream,CancellationToken token)
        {
            var prefix=new byte[4];await ReadExactly(stream,prefix,token).ConfigureAwait(false);int length=BitConverter.ToInt32(prefix,0);
            if(length<2||length>MaximumMessageBytes)throw new IOException("Некорректный размер сообщения CAD Batch.");
            var data=new byte[length];await ReadExactly(stream,data,token).ConfigureAwait(false);return WireJson.Read<BridgeMessage>(Encoding.UTF8.GetString(data));
        }
    }
}
