using Dialogue;
using UniRx;

namespace DialogueMenu
{
    public class DialogueMenuItemController
    {
        public DialogueMenuItemController(DialogueMenuMapItem dialogueMenuItem, DialogueController dialogueController)
        {
            dialogueMenuItem.View.Button.OnClickAsObservable()
                .Subscribe(_ => dialogueController.OpenDialogue(dialogueMenuItem.Data.Dialogue))
                .AddTo(dialogueMenuItem.View.Button);
        }
    }
}