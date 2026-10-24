using UnityEngine;
namespace Spacewars.Presentation
{
    // QA-only witness branch: reuse the exact ordinary resource/Unity JSON APIs
    // without changing the no-engine Foundation test assembly configuration.
    public static class ScoutWitnessEnvironment
    {
        public static string DataPath=>Application.dataPath;
        public static T Load<T>(string resource)=>JsonUtility.FromJson<T>(Resources.Load<TextAsset>(resource).text);
        public static string Json(object value)=>JsonUtility.ToJson(value,true);
    }
}
