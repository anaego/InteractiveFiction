using SceneNavigation;
using UnityEngine;

namespace SceneInitialization
{
    public class StartSceneInitializer : MonoBehaviour
    {
        [SerializeField] private SceneInitializationConfig _initializationConfig;

        private void Awake()
        {
            var sceneNavController = new SceneNavigationController();
            sceneNavController.LoadScene(_initializationConfig.MenuSceneName);
        }
    }
}
