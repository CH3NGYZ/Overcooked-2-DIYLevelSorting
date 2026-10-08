using System;
using UnityEngine;
using UnityEngine.UI;
using DIYUI = OC2DIYLevel.UIUtils;

namespace OC2DIYLevelSorting
{
    // A real expanding list using T17Button's input domain instead of the game's
    // T17DropDown, whose IT17EventHelper methods do not bind an event system.
    public sealed class SortingDropdown : MonoBehaviour
    {
        private MenuView view;
        private bool isDirection;
        private T17Button trigger;
        private T17Text caption;
        private GameObject popup;
        private GameObject blocker;
        private readonly T17Button[] choices = new T17Button[2];
        private readonly T17Text[] choiceLabels = new T17Text[2];
        private T17EventSystem system;
        private bool open;

        internal static SortingDropdown Create(MenuView view, string name, bool isDirection)
        {
            SortingDropdown dropdown = null;
            T17Button button = DIYUI.AddButton(view.Menu, name, string.Empty, string.Empty, delegate
            {
                try { if (dropdown != null) dropdown.Toggle(); }
                catch (Exception e) { SortingPlugin.Instance.Report("SortingDropdown.Toggle", "Listener", e); }
            });
            dropdown = button.gameObject.AddComponent<SortingDropdown>();
            dropdown.view = view;
            dropdown.isDirection = isDirection;
            dropdown.trigger = button;
            button.transform.SetParent(view.Toolbar, false);
            RectTransform triggerRect = button.GetComponent<RectTransform>();
            triggerRect.anchorMin = new Vector2(0, 1);
            triggerRect.anchorMax = new Vector2(1, 1);
            triggerRect.pivot = new Vector2(0.5f, 1);
            triggerRect.sizeDelta = new Vector2(0, 100);
            triggerRect.anchoredPosition = new Vector2(0, isDirection ? -112 : 0);
            dropdown.caption = button.transform.Find("Title").GetComponent<T17Text>();
            RectTransform titleRect = dropdown.caption.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.04f, 0);
            titleRect.anchorMax = new Vector2(0.96f, 1);
            titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
            dropdown.caption.resizeTextForBestFit = true;
            dropdown.caption.resizeTextMinSize = 14;
            dropdown.caption.resizeTextMaxSize = dropdown.caption.fontSize;
            dropdown.CreatePopup();
            dropdown.Refresh();
            return dropdown;
        }

        private int Selected
        {
            get { return isDirection ? (int)SortingPlugin.Instance.Direction : (int)SortingPlugin.Instance.Method; }
        }

        private void CreatePopup()
        {
            Transform parent = view.Menu.transform.Find("SettingsBody");
            blocker = new GameObject(name + "_Blocker", typeof(RectTransform), typeof(Image), typeof(T17Button));
            blocker.layer = gameObject.layer;
            blocker.transform.SetParent(parent, false);
            RectTransform blockerRect = blocker.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
            Image blockerImage = blocker.GetComponent<Image>();
            blockerImage.color = Color.clear;
            T17Button blockerButton = blocker.GetComponent<T17Button>();
            blockerButton.targetGraphic = blockerImage;
            blockerButton.transition = Selectable.Transition.None;
            Navigation blockerNavigation = blockerButton.navigation;
            blockerNavigation.mode = Navigation.Mode.None;
            blockerButton.navigation = blockerNavigation;
            blockerButton.onClick.AddListener(delegate { Close(true); });
            view.Menu.AddAllowedSelectables(blockerButton);
            view.Scroll.AddAllowedSelectables(blockerButton);
            blocker.SetActive(false);

            popup = new GameObject(name + "_Options", typeof(RectTransform), typeof(Image));
            popup.layer = gameObject.layer;
            popup.transform.SetParent(parent, false);
            popup.GetComponent<Image>().color = new Color(0.08f, 0.14f, 0.18f, 0.98f);
            RectTransform panel = popup.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 1);
            const float height = 64f;
            panel.sizeDelta = new Vector2(GetComponent<RectTransform>().rect.width, height * 2 + 16);
            for (int i = 0; i < 2; i++)
            {
                int selected = i;
                T17Button option = DIYUI.AddButton(view.Menu, name + "_Option" + i, string.Empty, string.Empty, delegate
                {
                    try { Close(true); SortingPlugin.Instance.SetSort(selected, isDirection); }
                    catch (Exception e) { SortingPlugin.Instance.Report("SortingDropdown.Choose", "Listener", e); }
                });
                choices[i] = option;
                choiceLabels[i] = option.transform.Find("Title").GetComponent<T17Text>();
                choiceLabels[i].resizeTextForBestFit = true;
                choiceLabels[i].resizeTextMinSize = 14;
                choiceLabels[i].resizeTextMaxSize = choiceLabels[i].fontSize;
                RectTransform optionTitle = choiceLabels[i].GetComponent<RectTransform>();
                optionTitle.anchorMin = new Vector2(0.04f, 0);
                optionTitle.anchorMax = new Vector2(0.96f, 1);
                optionTitle.offsetMin = optionTitle.offsetMax = Vector2.zero;
                option.transform.SetParent(popup.transform, false);
                RectTransform rect = option.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(0.5f, 1);
                rect.sizeDelta = new Vector2(-16, height);
                rect.anchoredPosition = new Vector2(0, -8 - i * height);
                view.Menu.AddAllowedSelectables(option);
                view.Scroll.AddAllowedSelectables(option);
            }
            Navigation first = choices[0].navigation;
            first.mode = Navigation.Mode.Explicit;
            first.selectOnUp = choices[1];
            first.selectOnDown = choices[1];
            first.selectOnLeft = first.selectOnRight = null;
            choices[0].navigation = first;
            Navigation second = first;
            second.selectOnUp = second.selectOnDown = choices[0];
            choices[1].navigation = second;
            popup.SetActive(false);
        }

        internal void Refresh()
        {
            string[] values = isDirection ? Language.Directions : Language.Methods;
            caption.SetNonLocalizedText(Language.Caption(isDirection ? Language.DirectionLabel : Language.MethodLabel, values[Selected]));
            for (int i = 0; i < choices.Length; i++) choiceLabels[i].SetNonLocalizedText(i == Selected ? Language.Selected(values[i]) : values[i]);
        }

        internal void BindEventSystem(T17EventSystem eventSystem)
        {
            system = eventSystem;
            if (system == null) return;
            trigger.SetEventSystem(system);
            blocker.GetComponent<T17Button>().SetEventSystem(system);
            foreach (T17Button choice in choices) choice.SetEventSystem(system);
        }

        internal void Toggle()
        {
            if (open) { Close(true); return; }
            view.CloseDropdowns(false);
            Refresh();
            BindEventSystem(view.Menu.CachedEventSystem ?? view.Scroll.CachedEventSystem);
            Vector3[] corners = new Vector3[4];
            GetComponent<RectTransform>().GetWorldCorners(corners);
            RectTransform rect = popup.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(GetComponent<RectTransform>().rect.width, rect.sizeDelta.y);
            rect.position = corners[0];
            blocker.transform.SetAsLastSibling();
            popup.transform.SetAsLastSibling();
            blocker.SetActive(true);
            popup.SetActive(true);
            open = true;
            if (system != null) system.SetSelectedGameObject(choices[Selected].gameObject);
        }

        internal bool Close(bool restoreFocus)
        {
            if (!open) return false;
            open = false;
            if (popup != null) popup.SetActive(false);
            if (blocker != null) blocker.SetActive(false);
            if (restoreFocus && system != null && trigger != null) system.SetSelectedGameObject(trigger.gameObject);
            return true;
        }

        internal bool OwnsSelection(GameObject selected)
        {
            return selected != null && (selected == gameObject || (popup != null && selected.transform.IsChildOf(popup.transform)));
        }

        internal void DestroyPopup()
        {
            Close(false);
            if (popup != null) UnityEngine.Object.DestroyImmediate(popup);
            if (blocker != null) UnityEngine.Object.DestroyImmediate(blocker);
        }

        private void OnDisable() { Close(false); }
        private void OnDestroy() { DestroyPopup(); }
    }
}
