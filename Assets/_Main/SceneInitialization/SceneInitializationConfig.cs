using UnityEngine;

namespace SceneInitialization
{
    [CreateAssetMenu(menuName = "ScriptableObjects/SceneInitializationConfig", fileName = "SceneInitializationConfig")]
    public class SceneInitializationConfig : ScriptableObject
    {
        [field: SerializeField] public string MenuSceneName = "MenuScene";
    }
}
