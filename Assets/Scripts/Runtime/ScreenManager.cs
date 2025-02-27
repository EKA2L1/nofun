/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using ScreenOrientation = Nofun.Settings.ScreenOrientation;

namespace Nofun
{
    public class ScreenManager: MonoBehaviour
    {
        [SerializeField]
        private GameObject controlMobilePotrait;

        [SerializeField]
        private GameObject controlMobileLandscape;

        [SerializeField]
        private RawImage displayPotrait;

        [SerializeField]
        private RawImage displayLandscape;

        [SerializeField]
        private PanelSettings landscapePanelSettings;

        [SerializeField]
        private PanelSettings potraitPanelSettings;

        private Settings.ScreenOrientation screenOrientation;

        public event System.Action<Settings.ScreenOrientation> ScreenOrientationChanged;
        private Coroutine confirmScreenSizeChangeCoroutine;
        private bool isConfirmingPotrait = false;

        public Settings.ScreenOrientation ScreenOrientation
        {
            get => screenOrientation;
            set
            {
                SetScreenOrientationDetail(value);
            }
        }

        public RawImage CurrentDisplay => (screenOrientation == Settings.ScreenOrientation.Potrait) ? displayPotrait : displayLandscape;

        public PanelSettings CurrentPanelSettings => (screenOrientation == Settings.ScreenOrientation.Potrait) ? potraitPanelSettings : landscapePanelSettings;

        private void UpdateCanvasOrientation()
        {
            ScreenOrientationChanged?.Invoke(screenOrientation);
        }

        private void SetScreenOrientationDetail(Settings.ScreenOrientation value)
        {
            if (screenOrientation == value)
            {
                return;
            }

            if (Application.isMobilePlatform)
            {
                Screen.orientation = (value == Settings.ScreenOrientation.Potrait) ? UnityEngine.ScreenOrientation.Portrait : UnityEngine.ScreenOrientation.LandscapeLeft;
                screenOrientation = value;

                UpdateCanvasOrientation();
            }
        }

        private void Awake()
        {
#if !UNITY_EDITOR
            if (Application.isMobilePlatform)
            {
                screenOrientation = (Screen.orientation == UnityEngine.ScreenOrientation.Portrait) ? Settings.ScreenOrientation.Potrait : Settings.ScreenOrientation.Landscape;
            }
            else
#endif
            {
                if (Screen.width > Screen.height)
                {
                    screenOrientation = Settings.ScreenOrientation.Landscape;
                }
                else
                {
                    screenOrientation = Settings.ScreenOrientation.Potrait;
                }
            }
        }

        private IEnumerator ConfirmScreenSizeChange(bool isConfirmingPotrait)
        {
            for (int i = 0; i < 2; i++)
            {
                if (isConfirmingPotrait && (Screen.width > Screen.height))
                {
                    confirmScreenSizeChangeCoroutine = null;
                    yield break;
                }
                else if (!isConfirmingPotrait && (Screen.width <= Screen.height))
                {
                    confirmScreenSizeChangeCoroutine = null;
                    yield break;
                }
                else
                {
                    yield return null;
                }
            }

            screenOrientation = isConfirmingPotrait ? ScreenOrientation.Potrait : ScreenOrientation.Landscape;
            confirmScreenSizeChangeCoroutine = null;

            UpdateCanvasOrientation();
        }

#if UNITY_EDITOR || (!UNITY_ANDROID && !UNITY_IOS)
        private void Update()
        {
            if ((Screen.width > Screen.height) && (screenOrientation != Settings.ScreenOrientation.Landscape))
            {
                if (confirmScreenSizeChangeCoroutine != null)
                {
                    if (!isConfirmingPotrait)
                    {
                        return;
                    }

                    StopCoroutine(confirmScreenSizeChangeCoroutine);
                }

                confirmScreenSizeChangeCoroutine = StartCoroutine(ConfirmScreenSizeChange(false));
                isConfirmingPotrait = false;
            }

            if ((Screen.width <= Screen.height) && (screenOrientation != Settings.ScreenOrientation.Potrait))
            {
                if (confirmScreenSizeChangeCoroutine != null)
                {
                    if (isConfirmingPotrait)
                    {
                        return;
                    }

                    StopCoroutine(confirmScreenSizeChangeCoroutine);
                }

                confirmScreenSizeChangeCoroutine = StartCoroutine(ConfirmScreenSizeChange(true));
                isConfirmingPotrait = true;
            }
        }
#endif

        private void Start()
        {
            if (!Application.isMobilePlatform)
            {
                controlMobileLandscape.SetActive(false);
                controlMobilePotrait.SetActive(false);
            }

            UpdateCanvasOrientation();
        }
    }
}
