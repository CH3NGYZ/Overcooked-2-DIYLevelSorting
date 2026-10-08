namespace OC2DIYLevelSorting
{
    // All player-visible extension text lives here. DIYLevel uses the same language rule.
    internal static class Language
    {
        internal const string MethodDescription = "Name or AddedTime (file LastWriteTimeUtc; info* for packages, each scene resource file for levels).";
        internal const string DirectionDescription = "Ascending or Descending. Equal values use a stable identity tie-break.";
        internal const string HistoryDescription = "Most recently clicked level identity; only this level and its package are marked. No successful load or completion is required. The legacy key name is retained.";
        internal const string ReturnDescription = "Return to the previous DIY level list after exiting a loaded custom kitchen. False keeps the original frontend destination. / 退出已进入的自定义关卡后回到原选关列表；关闭则保留原返回行为。";
        internal const string PinDescription = "Pinned package/level identities in pin order; excluded from automatic sorting. / 已置顶的关卡包与关卡，按置顶先后固定排列，不参与自动排序。";
        internal static string Pin { get { return Text("Pin", "置顶"); } }
        internal static string Unpin { get { return Text("Unpin", "取消置顶"); } }
        internal static string Text(string english, string chinese)
        {
            return OC2DIYLevel.UIUtils.GetLocalizedText(english, chinese);
        }
        internal static string MethodLabel { get { return Text("Sort by", "排序方法"); } }
        internal static string DirectionLabel { get { return Text("Direction", "排序方向"); } }
        internal static string[] Methods { get { return new string[] { Text("Name", "名称"), Text("Added time", "添加时间") }; } }
        internal static string[] Directions { get { return new string[] { Text("Ascending", "正序"), Text("Descending", "倒序") }; } }
        internal static string Caption(string label, string value) { return label + ":\n" + value + "  ▾"; }
        internal static string Selected(string value) { return "✓  " + value; }
    }
}
