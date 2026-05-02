using System.Collections.Generic;
using Dialogue;
using DialogueMenu;
using UnityEngine;
using UnityEngine.UI;

namespace DialogueCategory 
{
    public class DialogueCategoryView : MonoBehaviour
    {
        // TODO: public?
        [field: SerializeField] public Button Button { get; private set; }
        
        public void CloseDialogueMenu(GameObject dialogueMenuContainer)
        {
            dialogueMenuContainer.SetActive(false);
        }

        public List<DialogueMenuMapItem> OpenDialogueMenu(
            DialogueMenuItemSO[] dialogueMenuItems, DialogueMenuView dialogueMenuView)
        {
            var dialogueMenuMap = dialogueMenuView.SetupDialogueList(dialogueMenuItems);
            dialogueMenuView.Container.SetActive(true);
            return dialogueMenuMap;
        }
    }
}