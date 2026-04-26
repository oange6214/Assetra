namespace Assetra.Core.Models.Import;

/// <summary>
/// 匯入檔案來源格式。每個 enum 值對應一個 <c>IImportParser</c> 實作。
/// </summary>
public enum ImportFormat
{
    Generic,

    // Top 5 銀行（對帳單 → 支出 / 收入）
    CathayUnitedBank,
    EsunBank,
    CtbcBank,
    TaishinBank,
    FubonBank,

    // Top 5 券商（交易明細 → 投資交易）
    YuantaSecurities,
    FubonSecurities,
    KgiSecurities,
    SinoPacSecurities,
    CapitalSecurities,
}

public static class ImportFormatExtensions
{
    public static ImportSourceKind ToSourceKind(this ImportFormat format) => format switch
    {
        ImportFormat.CathayUnitedBank
            or ImportFormat.EsunBank
            or ImportFormat.CtbcBank
            or ImportFormat.TaishinBank
            or ImportFormat.FubonBank => ImportSourceKind.BankStatement,
        ImportFormat.YuantaSecurities
            or ImportFormat.FubonSecurities
            or ImportFormat.KgiSecurities
            or ImportFormat.SinoPacSecurities
            or ImportFormat.CapitalSecurities => ImportSourceKind.BrokerStatement,
        _ => ImportSourceKind.BankStatement,
    };
}
