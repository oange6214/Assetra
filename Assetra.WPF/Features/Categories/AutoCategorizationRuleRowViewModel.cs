using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Categories;

public partial class AutoCategorizationRuleRowViewModel : ObservableObject
{
    public Guid Id { get; init; }

    [ObservableProperty] private string _keywordPattern = string.Empty;
    [ObservableProperty] private Guid _categoryId;
    [ObservableProperty] private int _priority;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _matchCaseSensitive;

    // 顯示用：來自分類列表的快照（避免每次 binding 重新查表）
    [ObservableProperty] private string _categoryDisplay = string.Empty;

    // Edit-mode buffers
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editKeyword = string.Empty;
    [ObservableProperty] private Guid _editCategoryId;
    [ObservableProperty] private int _editPriority;
    [ObservableProperty] private bool _editCaseSensitive;
    [ObservableProperty] private string _editError = string.Empty;

    public AutoCategorizationRule ToModel() => new(
        Id: Id,
        KeywordPattern: KeywordPattern,
        CategoryId: CategoryId,
        Priority: Priority,
        IsEnabled: IsEnabled,
        MatchCaseSensitive: MatchCaseSensitive);

    public static AutoCategorizationRuleRowViewModel FromModel(AutoCategorizationRule r) => new()
    {
        Id = r.Id,
        KeywordPattern = r.KeywordPattern,
        CategoryId = r.CategoryId,
        Priority = r.Priority,
        IsEnabled = r.IsEnabled,
        MatchCaseSensitive = r.MatchCaseSensitive,
        EditKeyword = r.KeywordPattern,
        EditCategoryId = r.CategoryId,
        EditPriority = r.Priority,
        EditCaseSensitive = r.MatchCaseSensitive,
    };

    public void EnterEditMode()
    {
        EditKeyword = KeywordPattern;
        EditCategoryId = CategoryId;
        EditPriority = Priority;
        EditCaseSensitive = MatchCaseSensitive;
        EditError = string.Empty;
        IsEditing = true;
    }

    public void CancelEditMode()
    {
        EditError = string.Empty;
        IsEditing = false;
    }
}
