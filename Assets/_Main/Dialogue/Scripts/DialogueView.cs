using System;
using System.Collections.Generic;
using LightSide;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

namespace Dialogue
{
    public class DialogueView : MonoBehaviour
    {
        [field: SerializeField] public GameObject Container { get; private set; }
        [field: SerializeField] public Transform TextOptionsContainer { get; private set; }
        
        [SerializeField] private UniText _mainText;
        [SerializeField] private DialogueOptionView _textOptionPrefab;
        [SerializeField] private Button _fullscreenButton;
        
        private IDisposable _fullscreenButtonClickDisposable;
        private readonly List<GameObject> _currentOptions = new List<GameObject>();

        public void Setup(DialogueSO data)
        {
            ProcessNode(data.StartNode);
        }

        private void ProcessNode(DialogueNodeSO node)
        {
            RemovePreviousOptions();
            _mainText.Text = node.Text;
            foreach (var nextNode in node.NextNodes)
            {
                switch (nextNode.Condition)
                {
                    case DialogueNodeCondition.None:
                    {
                        _fullscreenButtonClickDisposable?.Dispose();
                        _fullscreenButtonClickDisposable = _fullscreenButton.OnClickAsObservable()
                            .Subscribe(_ => ProcessNode(nextNode));
                        _fullscreenButton.gameObject.SetActive(true);
                        break;
                    }
                    case DialogueNodeCondition.Choice:
                    {
                        _fullscreenButtonClickDisposable?.Dispose();
                        _fullscreenButton.gameObject.SetActive(false);
                        var optionView = Instantiate(_textOptionPrefab, TextOptionsContainer);
                        _currentOptions.Add(optionView.gameObject);
                        optionView.SetText(nextNode.Text);
                        optionView.SetButtonAction(() => ProcessNode(nextNode));
                        break;
                    }
                    default:
                        break;
                }
            }
        }

        private void RemovePreviousOptions()
        {
            foreach (GameObject currentOption in _currentOptions)
            {
                Destroy(currentOption);
            }
            _currentOptions.Clear();
        }

        private void OnDestroy()
        {
            _fullscreenButtonClickDisposable?.Dispose();
        }
    }
}