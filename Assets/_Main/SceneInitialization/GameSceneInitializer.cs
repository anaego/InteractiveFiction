using SceneNavigation;
using UnityEngine;

namespace SceneInitialization
{
    public class GameSceneInitializer : MonoBehaviour
    {
        [SerializeField] private SceneNavigationView _sceneNavigationView;
        // [SerializeField] private DialogueView _dialogueView;
        // [SerializeField] private DialogueConfig _dialogueConfig;
        // [SerializeField] private DialogueStarterView[] _dialogueStarterViews;

        private void Start()
        {
            InitializeSceneNavigation();
            // InitializeDialogueControllers();
        }

        void InitializeSceneNavigation()
        {
            var sceneNavigationController = new SceneNavigationController(_sceneNavigationView);
            sceneNavigationController.InitializeView();
        }

        // void InitializeDialogueControllers()
        // {
        //     var dialogueStarterControllers = new DialogueStarterController[_dialogueStarterViews.Length];
        //     for (var index = 0; index < _dialogueStarterViews.Length; index++)
        //     {
        //         var dialogueStarterView = _dialogueStarterViews[index];
        //         dialogueStarterControllers[index] = new DialogueStarterController(dialogueStarterView);
        //     }
        //     var dialogueController = new DialogueController(_dialogueView, _dialogueConfig, dialogueStarterControllers);
        // }
    }
}
