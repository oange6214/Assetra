using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.AddStock;

public partial class AddStockViewModel : ObservableObject, IDisposable
{
    private readonly IStockSearchService _search;
    private readonly CompositeDisposable _disposables = new();
    private readonly BehaviorSubject<string> _querySubject = new(string.Empty);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private StockSearchResult? _selectedResult;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<StockSearchResult> SearchResults { get; } = new();

    public string SearchQuery
    {
        get => _querySubject.Value;
        set
        {
            if (_querySubject.Value == value)
                return;
            OnPropertyChanging();
            _querySubject.OnNext(value);
            OnPropertyChanged();
        }
    }

    public event Action? CloseRequested;

    /// <summary>
    /// Clears search state so each dialog open starts fresh.
    /// Called by the host before showing the dialog.
    /// </summary>
    public void Reset()
    {
        _querySubject.OnNext(string.Empty);
        SearchResults.Clear();
        SelectedResult = null;
        ErrorMessage = string.Empty;
    }

    public AddStockViewModel(IStockSearchService search, IScheduler uiScheduler)
    {
        _search = search;

        _querySubject
            .Throttle(TimeSpan.FromMilliseconds(300), uiScheduler)
            .DistinctUntilChanged()
            .ObserveOn(uiScheduler)
            .Subscribe(PerformSearch)
            .DisposeWith(_disposables);
    }

    private void PerformSearch(string query)
    {
        SearchResults.Clear();
        SelectedResult = null;
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(query))
            return;

        var results = _search.Search(query);
        foreach (var r in results)
            SearchResults.Add(r);

        if (results.Count == 0)
            ErrorMessage = $"找不到股票代號或名稱含「{query}」的股票";
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        if (SelectedResult is null)
            return;
        WeakReferenceMessenger.Default.Send(new AddStockConfirmedMessage(SelectedResult));
        CloseRequested?.Invoke();
    }

    private bool CanAdd() => SelectedResult is not null;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    public void Dispose()
    {
        _disposables.Dispose();
        _querySubject.Dispose();
    }
}
