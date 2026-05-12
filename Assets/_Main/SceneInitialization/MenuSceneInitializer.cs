using Dialogue;
using DialogueCategory;
using DialogueMenu;
using SceneNavigation;
using ScreenNavigation;
using UnityEngine;

namespace SceneInitialization
{
    public class MenuSceneInitializer : MonoBehaviour
    {
        [SerializeField] private SceneNavigationView _sceneNavigationView;
        [SerializeField] private DialogueCategoryMappingItem[] _dialogueCategoryMap;
        // TODO: to separate view?
        [SerializeField] private DialogueMenuView _dialogueMenuView;
        [SerializeField] private DialogueView _dialogueView;
        [SerializeField] private ScreenNavigationView _homeScreen;
        [SerializeField] private ScreenNavigationView _dialogueMenuScreen;
        [SerializeField] private ScreenNavigationView _dialogueScreen;

        private ScreenNavigationController _screenNavigationController;

        void Awake()
        {
            var sceneNavigationController = new SceneNavigationController(_sceneNavigationView);
            _screenNavigationController = new ScreenNavigationController(_homeScreen);
            sceneNavigationController.InitializeView(); // TODO: call inside system?
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
                    dialogueCategory, _dialogueMenuView, dialogueController, 
                    _screenNavigationController, _dialogueMenuScreen, _dialogueScreen);
            }
        }
    }
}
