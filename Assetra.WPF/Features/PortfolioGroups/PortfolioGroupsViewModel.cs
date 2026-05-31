using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.PortfolioGroups;

/// <summary>
/// Portfolio-Groups-Refactor P2：群組 CRUD VM。功能上比 GoalsViewModel 簡單
/// （沒 progress / 沒 deadline），所以結構壓縮成單檔。Add/Edit 共用同一個 form
/// 模式（沿用 Goals / Categories 慣例）。
///
/// <para>系統預設群組（<see cref="PortfolioGroup.DefaultId"/>）可 rename / 改色 /
/// 改 sort，但 Remove 會被 repo guard 擋下並 throw。</para>
/// </summary>
public sealed partial class PortfolioGroupsViewModel : ObservableObject
{
    private readonly IPortfolioGroupRepository _repository;
    private readonly PortfolioGroupCatalog? _catalog;
    private readonly ObservableCollection<PortfolioGroupRowViewModel> _groups = [];

    public ReadOnlyObservableCollection<PortfolioGroupRowViewModel> Groups { get; }

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // ── Add / Edit form ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private Guid? _editingId;

    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formDescription = string.Empty;
    [ObservableProperty] private string _formColorHex = string.Empty;
    [ObservableProperty] private string? _formError;

    /// <summary>True 編輯模式（vs 新增）。</summary>
    public bool IsEditing => EditingId.HasValue;

    /// <summary>True 當編輯目標非系統群組，允許 Delete 按鈕。</summary>
    public bool CanDelete =>
        EditingId is { } id && _groups.Any(g => g.Id == id && !g.IsSystem);

    public bool HasGroups => Groups.Count > 0;

    // ── In-app confirm dialog (mirror Goals pattern) ──
    [ObservableProperty] private bool _isConfirmDialogOpen;
    [ObservableProperty] private string _confirmDialogMessage = string.Empty;
    private Func<Task>? _confirmDialogAction;

    [RelayCommand]
    private async Task ConfirmDialogYes()
    {
        IsConfirmDialogOpen = false;
        if (_confirmDialogAction is not null)
            await _confirmDialogAction();
        _confirmDialogAction = null;
    }

    [RelayCommand]
    private void ConfirmDialogNo()
    {
        IsConfirmDialogOpen = false;
        _confirmDialogAction = null;
    }

    public PortfolioGroupsViewModel(IPortfolioGroupRepository repository, PortfolioGroupCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _catalog = catalog;
        Groups = new ReadOnlyObservableCollection<PortfolioGroupRowViewModel>(_groups);
        _groups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGroups));
    }

    private Task RefreshCatalogAsync() =>
        _catalog?.RefreshAsync() ?? Task.CompletedTask;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
            return;
        IsLoading = true;
        try
        {
            var rows = await _repository.GetAllAsync().ConfigureAwait(true);
            _groups.Clear();
            foreach (var g in rows)
                _groups.Add(new PortfolioGroupRowViewModel(g));
            IsLoaded = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void StartAdd()
    {
        ResetForm();
        EditingId = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void StartEdit(PortfolioGroupRowViewModel? row)
    {
        if (row is null)
            return;
        EditingId = row.Id;
        FormName = row.Name;
        FormDescription = row.Description ?? string.Empty;
        FormColorHex = row.ColorHex ?? string.Empty;
        FormError = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void Cancel() => ResetForm();

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        var name = (FormName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            FormError = "請輸入群組名稱";
            return;
        }

        // 取既有 row 的 IsSystem flag（編輯模式才有）；新增一律 false
        var existing = EditingId is { } id ? _groups.FirstOrDefault(g => g.Id == id) : null;

        var group = new PortfolioGroup(
            Id: EditingId ?? Guid.NewGuid(),
            Name: name,
            ColorHex: string.IsNullOrWhiteSpace(FormColorHex) ? null : FormColorHex.Trim(),
            Description: string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
            IconKey: existing?.Group.IconKey,
            SortOrder: existing?.Group.SortOrder ?? _groups.Count,
            DefaultCashAccountId: existing?.Group.DefaultCashAccountId,
            IsSystem: existing?.Group.IsSystem ?? false);

        try
        {
            if (EditingId is { } _)
            {
                await _repository.UpdateAsync(group).ConfigureAwait(true);
                if (existing is not null)
                    existing.Group = group;
            }
            else
            {
                await _repository.AddAsync(group).ConfigureAwait(true);
                _groups.Add(new PortfolioGroupRowViewModel(group));
            }
            await RefreshCatalogAsync().ConfigureAwait(true);
            ResetForm();
        }
        catch (Exception ex)
        {
            FormError = ex.Message;
        }
    }

    [RelayCommand]
    private void Remove(PortfolioGroupRowViewModel? row)
    {
        if (row is null)
            return;
        if (row.IsSystem)
        {
            ErrorMessage = "預設群組無法刪除（可重新命名）";
            return;
        }

        ConfirmDialogMessage = $"確定要刪除「{row.Name}」？此操作無法復原。";
        _confirmDialogAction = async () =>
        {
            try
            {
                await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
                _groups.Remove(row);
                await RefreshCatalogAsync().ConfigureAwait(true);
                if (EditingId == row.Id)
                    ResetForm();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        };
        IsConfirmDialogOpen = true;
    }

    private void ResetForm()
    {
        EditingId = null;
        FormName = string.Empty;
        FormDescription = string.Empty;
        FormColorHex = string.Empty;
        FormError = null;
        IsFormOpen = false;
    }
}
