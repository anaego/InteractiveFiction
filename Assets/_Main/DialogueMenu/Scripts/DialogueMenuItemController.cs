using Dialogue;
using ScreenNavigation;
using UniRx;

namespace DialogueMenu
{
    public class DialogueMenuItemController
    {
        public DialogueMenuItemController(DialogueMenuMapItem dialogueMenuItem, DialogueController dialogueController, 
            ScreenNavigationController screenNavigationController, ScreenNavigationView dialogueScreen)
        {
            dialogueMenuItem.View.Button.OnClickAsObservable()
                .Subscribe(_ =>
                {
                    dialogueController.SetupDialogue(dialogueMenuItem.Data.Dialogue);
                    screenNavigationController.SetScreenActive(dialogueScreen);
                })
                .AddTo(dialogueMenuItem.View.Button);
        }
    }
}