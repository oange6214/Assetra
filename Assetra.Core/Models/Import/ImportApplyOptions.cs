namespace Assetra.Core.Models.Import;

/// <summary>
/// 將 <see cref="ImportBatch"/> 提交到資料庫時的輔助選項。
/// 由 UI 在提交前讓使用者選擇。
/// </summary>
public sealed record ImportApplyOptions(
    Guid? CashAccountId = null,
    string Exchange = "TWSE",
    string DefaultIncomeNote = "Imported income",
    string DefaultExpenseNote = "Imported expense");
