using System.Collections.Generic;
using Dialogue;
using DialogueMenu;
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

        public DialogueCategoryController(DialogueCategoryMappingItem dialogueCategory, DialogueMenuView dialogueMenu, DialogueController dialogueController)
        {
            _view = dialogueCategory.View;
            _dialogueController = dialogueController;
            dialogueCategory.View.Button.OnClickAsObservable()
                .Subscribe(_ => OpenDialogueMenuForCategory(dialogueCategory.Data, dialogueMenu))
                .AddTo(dialogueCategory.View);
        }

        private void OpenDialogueMenuForCategory(DialogueCategorySO dialogueCategoryData, DialogueMenuView dialogueMenu)
        {
            var dialogueMenuMap 
                = _view.OpenDialogueMenu(dialogueCategoryData.DialogueMenuItems, dialogueMenu);
            foreach (var dialogueMenuItem in dialogueMenuMap)
            {
                var dialogueMenuItemController = new DialogueMenuItemController(dialogueMenuItem, _dialogueController);
                _dialogueMenuControllerMap.Add(dialogueMenuItemController, dialogueMenuItem);
            }
        }
    }
}