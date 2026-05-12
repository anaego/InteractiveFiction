using System.Collections.Generic;
using Dialogue;
using DialogueMenu;
using ScreenNavigation;
using UniRx;
using UnityEngine;

namespace DialogueCategory
{
    public class DialogueCategoryController
    {
        private readonly DialogueCategoryView _view;

        // TODO: Use
        private Dictionary<DialogueMenuItemController, DialogueMenuMapItem> _dialogueMenuControllerMap = new();
        private DialogueController _dialogueController;

        public DialogueCategoryController(
            DialogueCategoryMappingItem dialogueCategory, DialogueMenuView dialogueMenuView, DialogueController dialogueController, 
            ScreenNavigationController screenNavigationController, ScreenNavigationView dialogueMenuScreen, ScreenNavigationView dialogueScreen)
        {
            _view = dialogueCategory.View;
            _dialogueController = dialogueController;
            dialogueCategory.View.Button.OnClickAsObservable()
                .Subscribe(_ =>
                    {
                        SetupDialogueMenuForCategory(dialogueCategory.Data, dialogueMenuView, screenNavigationController, dialogueScreen);
                        screenNavigationController.SetScreenActive(dialogueMenuScreen, () => Reset(dialogueMenuView));
                    })
                .AddTo(dialogueCategory.View);
        }

        private void Reset(DialogueMenuView dialogueMenuView)
        {
            dialogueMenuView.Reset();
            _dialogueMenuControllerMap.Clear();
        }

        private void SetupDialogueMenuForCategory(DialogueCategorySO dialogueCategoryData, DialogueMenuView dialogueMenu, 
            ScreenNavigationController screenNavigationController, ScreenNavigationView dialogueScreen)
        {
            var dialogueMenuMap 
                = _view.SetupDialogueMenu(dialogueCategoryData.DialogueMenuItems, dialogueMenu);
            foreach (var dialogueMenuItem in dialogueMenuMap)
            {
                var dialogueMenuItemController = new DialogueMenuItemController(dialogueMenuItem, _dialogueController, 
                    screenNavigationController, dialogueScreen);
                _dialogueMenuControllerMap.Add(dialogueMenuItemController, dialogueMenuItem);
            }
        }
    }
}