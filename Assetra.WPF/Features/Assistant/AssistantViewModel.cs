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
    private readonly ILocalizationService? _localization;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isAnswering;

    private readonly ObservableCollection<AssistantMessage> _messages = [];
    public ReadOnlyObservableCollection<AssistantMessage> Messages { get; }

    public IReadOnlyList<string> SuggestedQueries => _assistant.SuggestedQueries;

    public bool HasMessages => _messages.Count > 0;
    public bool HasNoMessages => _messages.Count == 0;

    public AssistantViewModel(IFinancialAssistant assistant, ILocalizationService? localization = null)
    {
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
        _localization = localization;
        Messages = new ReadOnlyObservableCollection<AssistantMessage>(_messages);
        _messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(HasNoMessages));
        };
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
    private void ClearConversation() => _messages.Clear();
}

/// <summary>對話列表的單一訊息（user 提問 vs assistant 回答）。</summary>
public sealed record AssistantMessage(bool IsUser, string Text, string Source)
{
    public bool IsAssistant => !IsUser;
    public bool HasSource => !string.IsNullOrWhiteSpace(Source);
}
