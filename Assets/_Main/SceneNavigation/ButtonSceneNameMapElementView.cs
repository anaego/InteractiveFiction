using System;
using UnityEngine;
using UnityEngine.UI;

namespace SceneNavigation
{
    [Serializable]
    public class ButtonSceneNameMapElementView
    {
        [field: SerializeField] public Button ChangeSceneButton { get; private set; }
        [field: SerializeField] public string SceneName { get; private set; }
    }
}
