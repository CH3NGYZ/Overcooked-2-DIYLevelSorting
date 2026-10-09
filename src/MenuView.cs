using System;
using System.Collections.Generic;
using System.Globalization;
using LevelEditorStub;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DIYUI = OC2DIYLevel.UIUtils;

namespace OC2DIYLevelSorting
{
    internal sealed class Row
    {
        internal T17Button Button;
        internal LevelSetInfoSO Set;
        internal LevelInfoSO Level;
        internal SortKey Key;
        internal GameObject Badge;
        internal T17Text NameText;
        internal T17Button Pin;
        internal T17Text PinText;
    }

    public sealed class MenuView : MonoBehaviour
    {
        internal FrontendOptionsMenu Menu;
        internal T17ScrollView Scroll;
        internal RectTransform Content;
        internal RectTransform Toolbar;
        private readonly List<Row> rows = new List<Row>();
        private readonly HashSet<T17Button> tracked = new HashSet<T17Button>();
        private readonly RefreshGate gate = new RefreshGate();
        private SortingDropdown method;
        private SortingDropdown direction;
        private bool clearing;
        private bool applying;
        private GameObject lastSelection;
        private int lastSelectionFrame = -1;
        private static Sprite circle;

        internal void Initialize(FrontendOptionsMenu menu)
        {
            Menu = menu;
            Content = menu.transform.Find(GameAccess.ContentPath) as RectTransform;
            Scroll = menu.transform.Find("SettingsBody/ContentPC").GetComponent<T17ScrollView>();
            if (Content == null || Scroll == null) throw new InvalidOperationException("DIYLevel menu content/scroll missing.");
            ContentChanges observer = Content.gameObject.AddComponent<ContentChanges>();
            observer.View = this;
            CreateToolbar();
            EnsureControls();
        }

        private void CreateToolbar()
        {
            RectTransform body = Menu.transform.Find("SettingsBody") as RectTransform;
            RectTransform scrollRect = Scroll.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(body);
            Vector3[] corners = new Vector3[4];
            scrollRect.GetWorldCorners(corners);
            Vector3 topLeft = body.InverseTransformPoint(corners[1]);
            const float width = 290f;
            const float gap = 24f;
            const float margin = 30f;
            float available = topLeft.x - body.rect.xMin;
            if (available < width + gap + margin)
            {
                float reserve = width + gap + margin - available;
                // Mirror the former right column, keeping the list's right edge in place.
                scrollRect.sizeDelta -= new Vector2(reserve, 0);
                scrollRect.anchoredPosition += new Vector2(reserve * (1f - scrollRect.pivot.x), 0);
                scrollRect.GetWorldCorners(corners);
                topLeft = body.InverseTransformPoint(corners[1]);
            }
            GameObject panel = new GameObject("DIYSorting_Toolbar", typeof(RectTransform));
            panel.layer = Menu.gameObject.layer;
            panel.transform.SetParent(body, false);
            Toolbar = panel.GetComponent<RectTransform>();
            Toolbar.anchorMin = Toolbar.anchorMax = new Vector2(0.5f, 0.5f);
            Toolbar.pivot = new Vector2(1, 1);
            Toolbar.sizeDelta = new Vector2(width, 212f);
            Toolbar.localPosition = new Vector3(topLeft.x - gap, topLeft.y, 0);
        }

        internal void Request() { if (!applying && !clearing) gate.Request(Time.unscaledTime); }
        internal void Tick() { if (gate.Ready(Time.unscaledTime)) Flush(); }

        internal void Add(T17Button button, LevelSetInfoSO set, LevelInfoSO level)
        {
            if (!tracked.Add(button)) return;
            PackageMetadata package = SortingPlugin.Instance.Metadata.Get(set);
            string identity = package.Identity;
            if (level != null && !package.LevelIds.TryGetValue(level, out identity))
                throw new InvalidOperationException("Level was not found in the selected package.");
            Row row = new Row();
            row.Button = button;
            row.Set = set;
            row.Level = level;
            long addedTicks = level == null ? package.AddedTicks : SortingPlugin.Instance.Metadata.GetLevelAddedTicks(set, level);
            row.Key = new SortKey(Name(row), addedTicks, identity);
            Transform label = button.transform.Find(level == null ? "LevelSetName" : "Title");
            row.NameText = label == null ? null : label.GetComponent<T17Text>();
            row.Badge = CreateBadge(button, level == null);
            CreatePin(row);
            rows.Add(row);
            if (level != null)
            {
                // Keep original callbacks and callbacks from other mods intact.
                string clickedId = identity;
                ClickTracker clickTracker = button.gameObject.AddComponent<ClickTracker>();
                clickTracker.Identity = identity;
                button.onClick.AddListener(delegate
                {
                    try { SortingPlugin.Instance.Record(clickedId); }
                    catch (Exception e) { SortingPlugin.Instance.Report("T17Button.onClick", "Listener", e); }
                });
            }
            RefreshBadge(row);
            Request();
        }

        private static string Name(Row row)
        {
            return row.Level != null ? DIYUI.GetLocalizedText(row.Level.levelName, row.Level.levelNameZH)
                : DIYUI.GetLocalizedText(row.Set.levelSetName, row.Set.levelSetNameZH);
        }

        private void CreatePin(Row row)
        {
            row.Pin = DIYUI.AddButton(Menu, "DIYSorting_Pin", string.Empty, string.Empty, delegate
            {
                try { SortingPlugin.Instance.TogglePin(row.Key.Identity); }
                catch (Exception e) { SortingPlugin.Instance.Report("DIYSorting_Pin.onClick", "Listener", e); }
            });
            row.Pin.transform.SetParent(row.Button.transform, false);
            RectTransform rect = row.Pin.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(104f, 42f);
            rect.anchoredPosition = new Vector2(-12f, 0);
            LayoutElement layout = row.Pin.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            row.PinText = row.Pin.transform.Find("Title").GetComponent<T17Text>();
            RectTransform title = row.PinText.GetComponent<RectTransform>();
            title.anchorMin = Vector2.zero;
            title.anchorMax = Vector2.one;
            title.offsetMin = new Vector2(4, 0);
            title.offsetMax = new Vector2(-4, 0);
            row.PinText.alignment = TextAnchor.MiddleCenter;
            row.PinText.resizeTextForBestFit = true;
            row.PinText.resizeTextMinSize = 12;
            row.PinText.resizeTextMaxSize = 22;
            RowPinControl owner = row.Pin.gameObject.AddComponent<RowPinControl>();
            owner.Owner = row.Button;
            SelectionTracker tracker = row.Pin.gameObject.AddComponent<SelectionTracker>();
            tracker.View = this;
            if (row.Level == null)
            {
                // Preserve native child paths used by DIYLevel/FastInit while
                // reserving the right-hand part of the metadata line for Pin.
                foreach (Transform child in row.Button.transform)
                {
                    if (child.GetComponent<T17Text>() == null) continue;
                    RectTransform textRect = child as RectTransform;
                    if (textRect == null) continue;
                    textRect.anchorMin = new Vector2(textRect.anchorMin.x * 0.82f, textRect.anchorMin.y);
                    textRect.anchorMax = new Vector2(textRect.anchorMax.x * 0.82f, textRect.anchorMax.y);
                }
            }
            else if (row.NameText != null)
            {
                RectTransform nameRect = row.NameText.GetComponent<RectTransform>();
                Vector2 end = nameRect.offsetMax;
                end.x -= 124f;
                nameRect.offsetMax = end;
            }
            RefreshPin(row);
        }

        private static void RefreshPin(Row row)
        {
            if (row.PinText != null) row.PinText.SetNonLocalizedText(SortingPlugin.Instance.Pins.IndexOf(row.Key.Identity) >= 0 ? Language.Unpin : Language.Pin);
        }

        private void EnsureControls()
        {
            if (method == null) method = SortingDropdown.Create(this, "DIYSorting_Method", false);
            if (direction == null) direction = SortingDropdown.Create(this, "DIYSorting_Direction", true);
        }

        internal void RefreshLabels()
        {
            EnsureControls();
            method.Refresh();
            direction.Refresh();
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                Row row = rows[i];
                if (row.Button == null || row.Set == null) { rows.RemoveAt(i); continue; }
                row.Key.Name = Name(row) ?? string.Empty;
                if (row.NameText != null) row.NameText.SetNonLocalizedText(row.Key.Name);
                RefreshBadge(row);
                RefreshPin(row);
            }
            Request();
        }

        internal void BeforeClear()
        {
            clearing = true;
            CloseDropdowns(false);
            rows.Clear();
            tracked.Clear();
            // The fixed toolbar and its dropdowns survive native content clears.
            GameAccess.Items(Scroll).Clear();
        }

        internal void AfterClear()
        {
            clearing = false;
            EnsureControls();
            Request();
        }

        internal void Flush()
        {
            if (clearing || Content == null) return;
            applying = true;
            try
            {
                EnsureControls();
                rows.RemoveAll(delegate(Row row) { return row.Button == null || row.Button.transform.parent != Content; });
                tracked.Clear();
                foreach (Row row in rows) tracked.Add(row.Button);
                SortKeyComparer comparer = new SortKeyComparer(SortingPlugin.Instance.Method, SortingPlugin.Instance.Direction,
                    Localization.GetLanguage() == SupportedLanguages.Chinese ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.GetCultureInfo("en-US"), SortingPlugin.Instance.Pins);
                rows.Sort(delegate(Row a, Row b) { return comparer.Compare(a.Key, b.Key); });
                HashSet<Transform> sorted = new HashSet<Transform>();
                foreach (Row row in rows) sorted.Add(row.Button.transform);
                List<Transform> fixedRows = new List<Transform>();
                Transform reload = null;
                for (int i = 0; i < Content.childCount; i++)
                {
                    Transform child = Content.GetChild(i);
                    if (child.name == "FastInit_Reload") { reload = child; continue; }
                    if (sorted.Contains(child)) continue;
                    fixedRows.Add(child);
                }
                int index = 0;
                if (reload != null) Move(reload, index++);
                foreach (Row row in rows) if (SortingPlugin.Instance.Pins.IndexOf(row.Key.Identity) >= 0) Move(row.Button.transform, index++);
                foreach (Transform child in fixedRows) Move(child, index++);
                foreach (Row row in rows)
                {
                    if (SortingPlugin.Instance.Pins.IndexOf(row.Key.Identity) < 0) Move(row.Button.transform, index++);
                    RefreshBadge(row);
                    RefreshPin(row);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(Content);
                SynchronizeNavigation();
            }
            finally { applying = false; gate.Complete(); }
        }

        private static void Move(Transform item, int index)
        {
            if (item.GetSiblingIndex() != index) item.SetSiblingIndex(index);
        }

        internal void SynchronizeNavigation()
        {
            List<RectTransform> cache = GameAccess.Items(Scroll);
            cache.Clear();
            List<Selectable> selectables = new List<Selectable>();
            T17EventSystem system = Menu.CachedEventSystem ?? Scroll.CachedEventSystem;
            GameObject selected = system == null ? null : system.currentSelectedGameObject;
            RowPinControl selectedPin = selected == null ? null : selected.GetComponent<RowPinControl>();
            GameObject selectedRow = selectedPin == null || selectedPin.Owner == null ? selected : selectedPin.Owner.gameObject;
            for (int i = 0; i < Content.childCount; i++)
            {
                Transform child = Content.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                Selectable selectable = child.GetComponent<Selectable>();
                RectTransform rect = child as RectTransform;
                if (selectable == null || rect == null) continue;
                cache.Add(rect);
                selectables.Add(selectable);
                SelectionTracker tracker = child.GetComponent<SelectionTracker>();
                if (tracker == null) tracker = child.gameObject.AddComponent<SelectionTracker>();
                tracker.View = this;
                Menu.AddAllowedSelectables(selectable);
                Scroll.AddAllowedSelectables(selectable);
                T17Button button = selectable as T17Button;
                if (button != null && system != null) button.SetEventSystem(system);
                T17Button pin = child.Find("DIYSorting_Pin") == null ? null : child.Find("DIYSorting_Pin").GetComponent<T17Button>();
                if (pin != null)
                {
                    if (system != null) pin.SetEventSystem(system);
                    Menu.AddAllowedSelectables(pin);
                    Scroll.AddAllowedSelectables(pin);
                }
            }
            for (int i = 0; i < selectables.Count; i++)
            {
                Navigation nav = selectables[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = i == 0 ? Scroll.m_BorderSelectables.selectOnUp : selectables[i - 1];
                nav.selectOnDown = i == selectables.Count - 1 ? Scroll.m_BorderSelectables.selectOnDown : selectables[i + 1];
                nav.selectOnLeft = method.GetComponent<Selectable>();
                Transform pinTransform = selectables[i].transform.Find("DIYSorting_Pin");
                T17Button pin = pinTransform == null ? null : pinTransform.GetComponent<T17Button>();
                nav.selectOnRight = pin == null ? Scroll.m_BorderSelectables.selectOnRight : pin;
                selectables[i].navigation = nav;
                if (pin != null)
                {
                    Navigation pinNav = nav;
                    pinNav.selectOnLeft = selectables[i];
                    pinNav.selectOnRight = Scroll.m_BorderSelectables.selectOnRight;
                    pin.navigation = pinNav;
                }
            }
            if (Scroll.m_BorderSelectables.selectOnDown != null && selectables.Count > 0)
            {
                Navigation nav = Scroll.m_BorderSelectables.selectOnDown.navigation;
                nav.selectOnUp = selectables[selectables.Count - 1];
                Scroll.m_BorderSelectables.selectOnDown.navigation = nav;
            }
            int current = selectedRow == null ? -1 : cache.FindIndex(delegate(RectTransform item) { return item.gameObject == selectedRow; });
            if (current < 0) current = Math.Min(Scroll.GetCurrentSelected(), Math.Max(0, cache.Count - 1));
            GameAccess.Current.SetValue(Scroll, current);
            GameAccess.Previous.SetValue(Scroll, current);
            GameAccess.LerpTime.SetValue(Scroll, 0f);
            GameAccess.DesiredPosition.SetValue(Scroll, (Vector2)Content.localPosition);
            if (selectedRow != null && selectedRow.transform.parent == Content && Menu.isActiveAndEnabled)
                Scroll.ScrollToEntry(selectedRow, false);
            method.BindEventSystem(system);
            direction.BindEventSystem(system);
            Menu.AddAllowedSelectables(method.GetComponent<Selectable>());
            Menu.AddAllowedSelectables(direction.GetComponent<Selectable>());
            Scroll.AddAllowedSelectables(method.GetComponent<Selectable>());
            Scroll.AddAllowedSelectables(direction.GetComponent<Selectable>());
            LinkToolbar(cache.Count == 0 ? null : cache[current].GetComponent<Selectable>());
            if (system != null && Menu.isActiveAndEnabled)
            {
                GameObject pending = system.GetPendingSelectedGameObject();
                GameObject effective = pending == null ? selected : pending;
                if (cache.Count == 0 && !method.OwnsSelection(effective) && !direction.OwnsSelection(effective))
                    system.SetSelectedGameObject(method.gameObject);
                else if (selected == null && pending == null && cache.Count > 0)
                    system.SetSelectedGameObject(cache[current].gameObject);
            }
        }

        private void LinkToolbar(Selectable returnTo)
        {
            Selectable methodButton = method.GetComponent<Selectable>();
            Selectable directionButton = direction.GetComponent<Selectable>();
            Navigation methodNavigation = methodButton.navigation;
            methodNavigation.mode = Navigation.Mode.Explicit;
            methodNavigation.selectOnUp = Scroll.m_BorderSelectables.selectOnUp;
            methodNavigation.selectOnDown = directionButton;
            methodNavigation.selectOnLeft = null;
            methodNavigation.selectOnRight = returnTo;
            methodButton.navigation = methodNavigation;
            Navigation directionNavigation = methodNavigation;
            directionNavigation.selectOnUp = methodButton;
            directionNavigation.selectOnDown = returnTo;
            directionButton.navigation = directionNavigation;
        }

        internal bool AcceptSelection(Selectable selectable, ref int index)
        {
            int actual = GameAccess.Items(Scroll).IndexOf(selectable.GetComponent<RectTransform>());
            if (actual < 0) return false;
            index = actual;
            LinkToolbar(selectable);
            if (lastSelection == selectable.gameObject && lastSelectionFrame == Time.frameCount) return false;
            lastSelection = selectable.gameObject;
            lastSelectionFrame = Time.frameCount;
            return true;
        }

        internal void NotifySelected(Selectable selectable)
        {
            try { GameAccess.SelectElement.Invoke(Scroll, new object[] { selectable, 0 }); }
            catch (Exception e) { SortingPlugin.Instance.Report("T17ScrollView.OnElementSelected", "SelectionTracker", e); }
        }

        internal GameObject RestoreFocus(string identity, float scrollPosition)
        {
            Flush();
            Row row = rows.Find(delegate(Row item) { return item.Key.Identity == identity; });
            if (row == null && rows.Count > 0) row = rows[0];
            if (row == null || Menu.CachedEventSystem == null) return null;
            Scroll.verticalNormalizedPosition = Mathf.Clamp01(scrollPosition);
            Menu.CachedEventSystem.SetSelectedGameObject(row.Button.gameObject);
            Scroll.ScrollToEntry(row.Button.gameObject, false);
            return row.Button.gameObject;
        }

        internal bool CloseDropdowns(bool restoreFocus)
        {
            bool closed = method != null && method.Close(restoreFocus);
            return (direction != null && direction.Close(restoreFocus)) || closed;
        }

        private void RefreshBadge(Row row)
        {
            if (row.Badge != null) row.Badge.SetActive(row.Level == null
                ? SortingPlugin.Instance.History.HasPackage(row.Key.Identity)
                : SortingPlugin.Instance.History.Contains(row.Key.Identity));
        }

        private static GameObject CreateBadge(T17Button button, bool package)
        {
            if (circle == null)
            {
                Texture2D texture = new Texture2D(24, 24, TextureFormat.ARGB32, false);
                Color[] pixels = new Color[24 * 24];
                for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
                {
                    float distance = new Vector2(x - 11.5f, y - 11.5f).magnitude;
                    pixels[y * 24 + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(11.5f - distance));
                }
                texture.SetPixels(pixels);
                texture.Apply();
                circle = Sprite.Create(texture, new Rect(0, 0, 24, 24), new Vector2(0.5f, 0.5f));
                UnityEngine.Object.DontDestroyOnLoad(texture);
                UnityEngine.Object.DontDestroyOnLoad(circle);
            }
            GameObject badge = new GameObject("DIYSorting_Clicked", typeof(RectTransform), typeof(Image));
            badge.layer = button.gameObject.layer;
            badge.transform.SetParent(button.transform, false);
            RectTransform rect = badge.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.025f, 0.5f);
            rect.sizeDelta = new Vector2(12, 12);
            Image image = badge.GetComponent<Image>();
            image.sprite = circle;
            image.color = new Color(0.75f, 1f, 0.5f, 1f);
            image.raycastTarget = false;
            if (!package)
            {
                Transform title = button.transform.Find("Title");
                if (title != null)
                {
                    RectTransform text = title as RectTransform;
                    text.offsetMin += new Vector2(22, 0);
                }
            }
            return badge;
        }

        private void OnDisable() { CloseDropdowns(false); }
        private void OnDestroy()
        {
            CloseDropdowns(false);
            if (SortingPlugin.Instance != null && !object.ReferenceEquals(Menu, null)) SortingPlugin.Instance.Forget(Menu);
        }
    }

    public sealed class RowPinControl : MonoBehaviour
    {
        internal T17Button Owner;
    }

    public sealed class ContentChanges : MonoBehaviour
    {
        internal MenuView View;
        private void OnTransformChildrenChanged() { if (View != null) View.Request(); }
    }

    public sealed class SelectionTracker : MonoBehaviour, ISelectHandler
    {
        internal MenuView View;
        public void OnSelect(BaseEventData eventData)
        {
            if (View != null) View.NotifySelected(GetComponent<Selectable>());
        }
    }

    public sealed class ClickTracker : MonoBehaviour
    {
        internal string Identity;
    }
}
