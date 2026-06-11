using System.Windows.Controls;

namespace Assetra.WPF.Features.Settings;

/// <summary>
/// 設定頁外殼：左側分類導覽 + 右側 ContentControl（依 <see cref="SettingsViewModel.SelectedCategory"/>
/// 切換子視圖）。各分類的欄位與其專屬的 code-behind（PasswordBox seed / slider commit /
/// passphrase 清除）都已搬到 <c>Categories/</c> 底下對應的 UserControl，故此處僅需
/// InitializeComponent。
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}
