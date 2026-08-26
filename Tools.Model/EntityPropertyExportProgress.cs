namespace Tools.Model
{
    public sealed class EntityPropertyExportProgress
    {
        public string Stage { get; init; } = string.Empty;
        public string CurrentObject { get; init; } = string.Empty;
        public int ProcessedObjectCount { get; init; }
        public int TotalObjectCount { get; init; }
        public long PropertyCount { get; init; }
        public bool IsIndeterminate => TotalObjectCount <= 0;
        public double Percentage => TotalObjectCount <= 0? 0d: Math.Min(100d, ProcessedObjectCount * 100d / TotalObjectCount);
    }
}
