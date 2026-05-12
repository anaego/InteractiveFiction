using System;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

namespace ScreenNavigation
{
    public class ScreenNavigationView : MonoBehaviour
    {
        [field: SerializeField] public GameObject ScreenContentContainer { get; private set; }
        
        [SerializeField] private Button _backButton;
        
        private IDisposable _actionDisposable;

        public void SetBackButtonAction(Action action)
        {
            if (_backButton == null)
            {
                return;
            }
            _actionDisposable?.Dispose();
            _actionDisposable = _backButton.OnClickAsObservable().Subscribe(_ => action.Invoke());
        }

        private void OnDestroy()
        {
            _actionDisposable?.Dispose();
        }
    }
}
