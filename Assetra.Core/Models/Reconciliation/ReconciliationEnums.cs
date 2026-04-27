namespace Assetra.Core.Models.Reconciliation;

public enum ReconciliationStatus
{
    /// <summary>仍在處理 diffs，未簽收。</summary>
    Open = 0,

    /// <summary>所有 diff 已處置且簽收，作業鎖定為唯讀。</summary>
    Resolved = 1,

    /// <summary>使用者主動歸檔，從活躍清單移除。</summary>
    Archived = 2,
}

public enum ReconciliationDiffKind
{
    /// <summary>對帳單有，但帳上沒有對應 trade。</summary>
    Missing = 0,

    /// <summary>帳上有 trade，但對帳單未列。</summary>
    Extra = 1,

    /// <summary>金額或日期細節對不上但屬於同一筆。</summary>
    AmountMismatch = 2,
}

public enum ReconciliationDiffResolution
{
    /// <summary>尚未處置。</summary>
    Pending = 0,

    /// <summary>已依對帳單建立 trade（針對 Missing）。</summary>
    Created = 1,

    /// <summary>已刪除帳上多出的 trade（針對 Extra）。</summary>
    Deleted = 2,

    /// <summary>已手動修正，標記為解決（任一 kind 皆適用）。</summary>
    MarkedResolved = 3,

    /// <summary>標記為忽略，不會再出現在待辦清單。</summary>
    Ignored = 4,

    /// <summary>已以對帳單金額覆蓋既有 trade（針對 AmountMismatch）。</summary>
    OverwrittenFromStatement = 5,
}
