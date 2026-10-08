namespace OC2DIYLevelSorting
{
    // All player-visible extension text lives here. DIYLevel uses the same language rule.
    internal static class Language
    {
        internal const string MethodDescription = "Name or AddedTime (file LastWriteTimeUtc; info* for packages, each scene resource file for levels).";
        internal const string DirectionDescription = "Ascending or Descending. Equal values use a stable identity tie-break.";
        internal const string HistoryDescription = "Most recently clicked level identity; only this level and its package are marked. No successful load or completion is required. The legacy key name is retained.";
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
