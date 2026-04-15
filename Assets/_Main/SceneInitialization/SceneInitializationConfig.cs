using UnityEngine;

namespace SceneInitialization
{
    [CreateAssetMenu(menuName = "Scriptable Objects/SceneInitializationConfig", fileName = "SceneInitializationConfig")]
    public class SceneInitializationConfig : ScriptableObject
    {
        [field: SerializeField] public string MenuSceneName = "MenuScene";
    }
}
