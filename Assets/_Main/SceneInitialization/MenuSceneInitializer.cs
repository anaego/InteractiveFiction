using Dialogue;
using DialogueCategory;
using DialogueMenu;
using SceneNavigation;
using UnityEngine;

namespace SceneInitialization
{
    public class MenuSceneInitializer : MonoBehaviour
    {
        [SerializeField] private SceneNavigationView _sceneNavigationView;
        [SerializeField] private DialogueCategoryMappingItem[] _dialogueCategoryMap;
        [SerializeField] private DialogueMenuView _dialogueMenuView;
        [SerializeField] private DialogueView _dialogueView;

        void Awake()
        {
            var sceneNavigationController = new SceneNavigationController(_sceneNavigationView);
            sceneNavigationController.InitializeView();
            InitializeDialogueControllers();
        }
        
        void InitializeDialogueControllers()
        {
            var dialogueController = new DialogueController(_dialogueView);
            var dialogueStarterControllers = new DialogueCategoryController[_dialogueCategoryMap.Length];
            for (var index = 0; index < _dialogueCategoryMap.Length; index++)
            {
                var dialogueCategory = _dialogueCategoryMap[index];
                dialogueStarterControllers[index] = new DialogueCategoryController(
                    dialogueCategory, _dialogueMenuView, dialogueController);
            }
        }
    }
}
