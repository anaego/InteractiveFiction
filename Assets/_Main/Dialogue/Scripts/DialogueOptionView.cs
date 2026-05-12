using System;
using LightSide;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

namespace Dialogue
{
    public class DialogueOptionView : MonoBehaviour
    {
        [SerializeField] private UniText _text;
        [SerializeField] private Button _button;
        
        private IDisposable _actionDisposable;

        public void SetText(string text)
        {
            _text.Text = text;
        }

        public void SetButtonAction(Action action)
        { 
            _actionDisposable?.Dispose();
            _actionDisposable = _button.OnClickAsObservable().Subscribe(_ => action.Invoke());
        }

        private void OnDestroy()
        {
            _actionDisposable?.Dispose();
        }
    }
}