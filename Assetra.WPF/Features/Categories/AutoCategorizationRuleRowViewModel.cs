using Assetra.Core.DomainServices;
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

    [ObservableProperty] private string? _name;
    [ObservableProperty] private AutoCategorizationMatchField _matchField = AutoCategorizationMatchField.AnyText;
    [ObservableProperty] private AutoCategorizationMatchType _matchType = AutoCategorizationMatchType.Contains;
    [ObservableProperty] private AutoCategorizationScope _appliesTo = AutoCategorizationScope.Both;

    // 顯示用：來自分類列表的快照
    [ObservableProperty] private string _categoryDisplay = string.Empty;

    // Edit-mode buffers
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editKeyword = string.Empty;
    [ObservableProperty] private Guid _editCategoryId;
    [ObservableProperty] private int _editPriority;
    [ObservableProperty] private bool _editCaseSensitive;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private AutoCategorizationMatchField _editMatchField = AutoCategorizationMatchField.AnyText;
    [ObservableProperty] private AutoCategorizationMatchType _editMatchType = AutoCategorizationMatchType.Contains;
    [ObservableProperty] private bool _editAppliesToManual = true;
    [ObservableProperty] private bool _editAppliesToImport = true;
    [ObservableProperty] private string _editError = string.Empty;

    // Live-test inputs
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveTestCounterpartyHit))]
    private string _liveTestCounterparty = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveTestMemoHit))]
    private string _liveTestMemo = string.Empty;

    public bool LiveTestCounterpartyHit => EvaluateLiveTest(LiveTestCounterparty, isMemo: false);
    public bool LiveTestMemoHit => EvaluateLiveTest(LiveTestMemo, isMemo: true);

    public AutoCategorizationRule ToModel() => new(
        Id: Id,
        KeywordPattern: KeywordPattern,
        CategoryId: CategoryId,
        Priority: Priority,
        IsEnabled: IsEnabled,
        MatchCaseSensitive: MatchCaseSensitive,
        Name: Name,
        MatchField: MatchField,
        MatchType: MatchType,
        AppliesTo: AppliesTo);

    public static AutoCategorizationRuleRowViewModel FromModel(AutoCategorizationRule r) => new()
    {
        Id = r.Id,
        KeywordPattern = r.KeywordPattern,
        CategoryId = r.CategoryId,
        Priority = r.Priority,
        IsEnabled = r.IsEnabled,
        MatchCaseSensitive = r.MatchCaseSensitive,
        Name = r.Name,
        MatchField = r.MatchField,
        MatchType = r.MatchType,
        AppliesTo = r.AppliesTo,
        EditKeyword = r.KeywordPattern,
        EditCategoryId = r.CategoryId,
        EditPriority = r.Priority,
        EditCaseSensitive = r.MatchCaseSensitive,
        EditName = r.Name ?? string.Empty,
        EditMatchField = r.MatchField,
        EditMatchType = r.MatchType,
        EditAppliesToManual = (r.AppliesTo & AutoCategorizationScope.Manual) != 0,
        EditAppliesToImport = (r.AppliesTo & AutoCategorizationScope.Import) != 0,
    };

    public void EnterEditMode()
    {
        EditKeyword = KeywordPattern;
        EditCategoryId = CategoryId;
        EditPriority = Priority;
        EditCaseSensitive = MatchCaseSensitive;
        EditName = Name ?? string.Empty;
        EditMatchField = MatchField;
        EditMatchType = MatchType;
        EditAppliesToManual = (AppliesTo & AutoCategorizationScope.Manual) != 0;
        EditAppliesToImport = (AppliesTo & AutoCategorizationScope.Import) != 0;
        LiveTestCounterparty = string.Empty;
        LiveTestMemo = string.Empty;
        EditError = string.Empty;
        IsEditing = true;
    }

    public void CancelEditMode()
    {
        EditError = string.Empty;
        IsEditing = false;
    }

    public AutoCategorizationScope ResolveEditAppliesTo()
    {
        var scope = AutoCategorizationScope.None;
        if (EditAppliesToManual) scope |= AutoCategorizationScope.Manual;
        if (EditAppliesToImport) scope |= AutoCategorizationScope.Import;
        return scope == AutoCategorizationScope.None ? AutoCategorizationScope.Both : scope;
    }

    partial void OnEditKeywordChanged(string value)
    {
        OnPropertyChanged(nameof(LiveTestCounterpartyHit));
        OnPropertyChanged(nameof(LiveTestMemoHit));
    }
    partial void OnEditMatchFieldChanged(AutoCategorizationMatchField value)
    {
        OnPropertyChanged(nameof(LiveTestCounterpartyHit));
        OnPropertyChanged(nameof(LiveTestMemoHit));
    }
    partial void OnEditMatchTypeChanged(AutoCategorizationMatchType value)
    {
        OnPropertyChanged(nameof(LiveTestCounterpartyHit));
        OnPropertyChanged(nameof(LiveTestMemoHit));
    }
    partial void OnEditCaseSensitiveChanged(bool value)
    {
        OnPropertyChanged(nameof(LiveTestCounterpartyHit));
        OnPropertyChanged(nameof(LiveTestMemoHit));
    }

    private bool EvaluateLiveTest(string? input, bool isMemo)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(EditKeyword)) return false;
        var preview = new AutoCategorizationRule(
            Id: Guid.Empty,
            KeywordPattern: EditKeyword,
            CategoryId: Guid.Empty,
            Priority: 0,
            IsEnabled: true,
            MatchCaseSensitive: EditCaseSensitive,
            Name: null,
            MatchField: EditMatchField,
            MatchType: EditMatchType,
            AppliesTo: AutoCategorizationScope.Both);
        var ctx = new AutoCategorizationContext(
            Note: null,
            Counterparty: isMemo ? null : input,
            Memo: isMemo ? input : null,
            Source: AutoCategorizationScope.Import);
        return AutoCategorizationEngine.Match(ctx, new[] { preview }) is not null;
    }
}
