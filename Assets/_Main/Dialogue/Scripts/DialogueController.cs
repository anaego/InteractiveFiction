namespace Dialogue
{
    public class DialogueController
    {
        private DialogueView _view;
        
        public DialogueController(DialogueView dialogueView)
        {
            _view = dialogueView;
        }
        
        public void OpenDialogue(DialogueSO data)
        {
            _view.Setup(data);
            _view.Container.SetActive(true);
        }
    }
}