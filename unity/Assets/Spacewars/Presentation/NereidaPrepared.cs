using System;
using System.IO;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class NereidaPreview
    {
        PreparedSceneInstance prepared;
        public bool PreparationCacheHit{get;private set;}
        bool TryPrepared(string key)
        {
            var bytes=PreparedMapCache.Read("Preview-Nereida",key);if(bytes==null)return false;
            PreparedScene data;try{data=PreparedSceneCodec.Decode(bytes);}catch(Exception e)when(e is IOException||e is InvalidDataException||e is FormatException||e is ArgumentException){Debug.LogWarning("Discarded prepared Nereida: "+e.Message);return false;}
            prepared=new PreparedSceneInstance();LoadingPreparation.Drain(prepared.Restore(data,transform));PreparationCacheHit=true;return true;
        }
        void SavePrepared(string key)
        {
            try{PreparedMapCache.Write("Preview-Nereida",key,PreparedSceneCodec.Encode(key,PreparedSceneCodec.Capture(transform)));}
            catch(Exception e)when(e is IOException||e is InvalidDataException||e is FormatException||e is ArgumentException||e is UnityException){Debug.LogWarning("Preview prepared without persistent cache: "+e.Message);}
        }
    }
}
