using UnityEngine;

namespace SceneNavigation
{
    public class SceneNavigationView : MonoBehaviour
    {
        [field: SerializeField] public ButtonSceneNameMapElementView[] ButtonSceneNameMap { get; private set; }
    }
}
