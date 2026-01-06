using SceneNavigation;
using UnityEngine;

namespace SceneInitialization
{
    public class MenuSceneInitializer : MonoBehaviour
    {
        [SerializeField] private SceneNavigationView _sceneNavigationView;

        void Awake()
        {
            var sceneNavigationController = new SceneNavigationController(_sceneNavigationView);
            sceneNavigationController.InitializeView();
        }
    }
}
