using UniRx;
using UnityEngine.SceneManagement;

namespace SceneNavigation
{
    public class SceneNavigationController
    {
        private readonly SceneNavigationView _view;

        public SceneNavigationController()
        {
            InitializeView();
        }

        public SceneNavigationController(SceneNavigationView sceneNavigationView) : this()
        {
            _view = sceneNavigationView;
        }

        public void LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        public void InitializeView()
        {
            if (_view == null)
            {
                return;
            }
            foreach (var buttonSceneNameMapElement in _view.ButtonSceneNameMap)
            {
                buttonSceneNameMapElement.ChangeSceneButton.OnClickAsObservable()
                    .Subscribe(_ => LoadScene(buttonSceneNameMapElement.SceneName)).AddTo(_view);
            }
        }
    }
}
