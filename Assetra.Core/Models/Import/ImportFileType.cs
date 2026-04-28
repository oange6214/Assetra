namespace Assetra.Core.Models.Import;

public enum ImportFileType
{
    Csv,
    Excel,
    /// <summary>銀行 / 信用卡 PDF 對帳單。文字模式直接抽取，圖片模式經 OCR。v0.19.0 新增；具體 parser 留待 v0.19.1+。</summary>
    Pdf,
}
