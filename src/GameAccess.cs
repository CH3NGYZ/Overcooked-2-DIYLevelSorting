using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LevelEditorStub;
using UnityEngine;

namespace OC2DIYLevelSorting
{
    internal static class GameAccess
    {
        internal const string ContentPath = "SettingsBody/ContentPC/Viewport/Content";
        internal static readonly FieldInfo SetMenu = AccessTools.Field(typeof(OC2DIYLevel.DIYLevelEntryUI), "levelSetSelectionMenu");
        internal static readonly FieldInfo LevelMenu = AccessTools.Field(typeof(OC2DIYLevel.DIYLevelEntryUI), "levelSelectionMenu");
        internal static readonly FieldInfo BaseUid = AccessTools.Field(typeof(LevelSetInfoSO), "baseUID");
        internal static readonly FieldInfo Selectables = AccessTools.Field(typeof(T17NavigableGrid), "m_ContentSelectables");
        internal static readonly FieldInfo Current = AccessTools.Field(typeof(T17NavigableGrid), "m_CurrentSelected");
        internal static readonly FieldInfo Previous = AccessTools.Field(typeof(T17NavigableGrid), "m_PreviousSelected");
        internal static readonly FieldInfo LerpTime = AccessTools.Field(typeof(T17ScrollView), "m_LerpTime");
        internal static readonly FieldInfo DesiredPosition = AccessTools.Field(typeof(T17ScrollView), "m_DesiredPosition");
        internal static readonly MethodInfo SelectElement = AccessTools.Method(typeof(T17ScrollView), "OnElementSelected");

        internal static List<RectTransform> Items(T17ScrollView scroll)
        {
            return (List<RectTransform>)Selectables.GetValue(scroll);
        }
    }
}
