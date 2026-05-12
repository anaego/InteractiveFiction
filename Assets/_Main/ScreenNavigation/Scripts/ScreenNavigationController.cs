using System;
using System.Collections.Generic;

namespace ScreenNavigation
{
    public class ScreenNavigationController
    {
        private readonly Dictionary<ScreenNavigationView, ScreenNavigationView> _backScreenMapping = new();
        private ScreenNavigationView _currentScreen;

        public ScreenNavigationController(ScreenNavigationView firstScreenView, Action onGoBackAction = null)
        {
            SetScreenActive(firstScreenView, onGoBackAction ?? (() => { }));
        }

        public void SetScreenActive(ScreenNavigationView screen, Action onGoBackAction = null)
        {
            _currentScreen?.ScreenContentContainer.SetActive(false);
            _backScreenMapping[screen] = _currentScreen;
            _currentScreen = screen;
            SetGoBackAction(_currentScreen, onGoBackAction ?? (() => { }));
            _currentScreen.ScreenContentContainer.SetActive(true);
        }

        private void SetGoBackAction(ScreenNavigationView screen, Action onGoBackAction)
        {
            var backScreen = _backScreenMapping[screen];
            if (backScreen == null)
            {
                return;
            }
            screen.SetBackButtonAction(
                () =>
                {
                    screen.ScreenContentContainer.SetActive(false);
                    backScreen.ScreenContentContainer.SetActive(true);
                    _backScreenMapping.Remove(screen);
                    _currentScreen = backScreen;
                    onGoBackAction?.Invoke();
                });
        }
    }
}