using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Assistant;

/// <summary>
/// AI 財務助手頁的 ViewModel。把 <see cref="IFinancialAssistant"/> 包裝成
/// 對話式 UI：使用者輸入 → AnswerAsync → AssistantMessage 加進對話列表。
///
/// <para>
/// MVP 不持久化對話記錄；下一階段（搭配 LLM）可加 history 持久化 + 跨 session
/// 查詢回顧。
/// </para>
/// </summary>
public sealed partial class AssistantViewModel : ObservableObject
{
    private readonly IFinancialAssistant _assistant;
    private readonly IAssistantInsightService? _insights;
    private readonly IAssistantHistoryRepository? _history;
    private readonly ILocalizationService? _localization;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isAnswering;

    private readonly ObservableCollection<AssistantMessage> _messages = [];
    public ReadOnlyObservableCollection<AssistantMessage> Messages { get; }

    private readonly ObservableCollection<AssistantInsight> _insightCards = [];
    public ReadOnlyObservableCollection<AssistantInsight> InsightCards { get; }

    public IReadOnlyList<string> SuggestedQueries => _assistant.SuggestedQueries;

    public bool HasMessages => _messages.Count > 0;
    public bool HasNoMessages => _messages.Count == 0;
    public bool HasInsights => _insightCards.Count > 0;

    public AssistantViewModel(
        IFinancialAssistant assistant,
        IAssistantInsightService? insights = null,
        IAssistantHistoryRepository? history = null,
        ILocalizationService? localization = null)
    {
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
        _insights = insights;
        _history = history;
        _localization = localization;
        Messages = new ReadOnlyObservableCollection<AssistantMessage>(_messages);
        InsightCards = new ReadOnlyObservableCollection<AssistantInsight>(_insightCards);
        _messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(HasNoMessages));
        };
        _insightCards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasInsights));
    }

    [RelayCommand]
    public async Task LoadInsightsAsync()
    {
        if (_insights is null) return;
        try
        {
            var snapshot = await _insights.GetCurrentInsightsAsync().ConfigureAwait(true);
            _insightCards.Clear();
            foreach (var i in snapshot) _insightCards.Add(i);
        }
        catch
        {
            // Insight loading is best-effort — never block the assistant UI.
        }
    }

    /// <summary>Dismiss a single insight card from the visible list.</summary>
    [RelayCommand]
    private void DismissInsight(AssistantInsight? insight)
    {
        if (insight is null) return;
        _insightCards.Remove(insight);
    }

    private bool CanSend() => !IsAnswering && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0) return;

        var locale = _localization?.CurrentLanguage ?? "zh-TW";
        _messages.Add(new AssistantMessage(IsUser: true, Text: text, Source: string.Empty));
        InputText = string.Empty;
        IsAnswering = true;
        try
        {
            var response = await _assistant.AnswerAsync(new FinancialAssistantQuery(text, locale))
                .ConfigureAwait(true);
            _messages.Add(new AssistantMessage(
                IsUser: false,
                Text: response.Answer,
                Source: response.Source));

            // Persist (best-effort, never block the UI on failure).
            if (_history is not null)
            {
                try
                {
                    await _history.AddAsync(new AssistantHistoryEntry(
                        Id: Guid.NewGuid(),
                        AskedAt: DateTime.UtcNow,
                        UserText: text,
                        AssistantText: response.Answer,
                        Source: response.Source ?? string.Empty)).ConfigureAwait(true);
                }
                catch { /* swallow — history is auxiliary */ }
            }
        }
        catch (Exception ex)
        {
            _messages.Add(new AssistantMessage(
                IsUser: false,
                Text: $"執行查詢時發生錯誤：{ex.Message}",
                Source: "Error"));
        }
        finally
        {
            IsAnswering = false;
        }
    }

    [RelayCommand]
    private void UseSuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        InputText = suggestion;
    }

    [RelayCommand]
    private async Task ClearConversationAsync()
    {
        _messages.Clear();
        if (_history is not null)
        {
            try { await _history.ClearAsync(); }
            catch { /* swallow */ }
        }
    }

    /// <summary>
    /// Loads recent persisted conversation entries (most-recent first) and
    /// re-creates the chat bubbles in chronological order. Called once on view load.
    /// </summary>
    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        if (_history is null || _messages.Count > 0) return;
        try
        {
            var entries = await _history.GetRecentAsync(limit: 30).ConfigureAwait(true);
            // entries is desc; replay chronologically
            foreach (var e in entries.Reverse())
            {
                _messages.Add(new AssistantMessage(IsUser: true, Text: e.UserText, Source: string.Empty));
                _messages.Add(new AssistantMessage(IsUser: false, Text: e.AssistantText, Source: e.Source));
            }
        }
        catch { /* swallow — history load is auxiliary */ }
    }
}

/// <summary>對話列表的單一訊息（user 提問 vs assistant 回答）。</summary>
public sealed record AssistantMessage(bool IsUser, string Text, string Source)
{
    public bool IsAssistant => !IsUser;
    public bool HasSource => !string.IsNullOrWhiteSpace(Source);
}
