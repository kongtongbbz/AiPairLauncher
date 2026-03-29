using System.ComponentModel;
using AiPairLauncher.App.Models;
using AiPairLauncher.App.Services;

namespace AiPairLauncher.App.ViewModels.Pages;

public sealed class AutomationPageViewModel : INotifyPropertyChanged
{
    private readonly TaskMdParser _taskMdParser = new();
    private TaskMdSnapshot _snapshot = new();

    public AutomationPageViewModel(MainWindowViewModel core, SharedSessionState sharedState)
    {
        Core = core ?? throw new ArgumentNullException(nameof(core));
        SharedState = sharedState ?? throw new ArgumentNullException(nameof(sharedState));
        Core.PropertyChanged += CoreOnPropertyChanged;
        RefreshSnapshot();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel Core { get; }

    public SharedSessionState SharedState { get; }

    public TaskMdSnapshot Snapshot => _snapshot;

    public string CurrentTaskRole => _snapshot.CurrentTaskRole;

    public string CurrentTaskDependencies => _snapshot.CurrentTaskDependencies;

    public string CurrentTaskRisk => _snapshot.CurrentTaskRisk;

    public string CurrentTaskExecutionSummary => _snapshot.CurrentTaskExecutionSummary;

    public string ReviewSummary => _snapshot.ReviewSummary;

    public int ReviewSectionCount => _snapshot.ReviewSectionCount;

    public bool IsPhase1Active => string.Equals(Core.AutomationPhaseLabel, "Phase 1 · 项目调研", StringComparison.Ordinal);

    public bool IsPhase2Active => string.Equals(Core.AutomationPhaseLabel, "Phase 2 · 计划编排", StringComparison.Ordinal);

    public bool IsPhase3Active => string.Equals(Core.AutomationPhaseLabel, "Phase 3 · 任务执行", StringComparison.Ordinal);

    public bool IsPhase4Active => string.Equals(Core.AutomationPhaseLabel, "Phase 4 · 复核验收", StringComparison.Ordinal);

    public string Phase1StateText => BuildPhaseStateText(1, IsPhase1Active);

    public string Phase2StateText => BuildPhaseStateText(2, IsPhase2Active);

    public string Phase3StateText => BuildPhaseStateText(3, IsPhase3Active);

    public string Phase4StateText => BuildPhaseStateText(4, IsPhase4Active);

    public void RefreshSnapshot()
    {
        var taskMdPath = Core.AutomationTaskMdPath;
        if (string.IsNullOrWhiteSpace(taskMdPath) || string.Equals(taskMdPath, "暂无", StringComparison.Ordinal))
        {
            _snapshot = new TaskMdSnapshot();
            RaiseSnapshotChanged();
            return;
        }

        _snapshot = _taskMdParser.ParseFile(taskMdPath).Snapshot;
        RaiseSnapshotChanged();
    }

    private void CoreOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.AutomationTaskMdPath), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(MainWindowViewModel.AutomationTaskMdStatus), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(MainWindowViewModel.AutomationCurrentTaskRef), StringComparison.Ordinal))
        {
            RefreshSnapshot();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.AutomationPhaseLabel), StringComparison.Ordinal))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPhase1Active)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPhase2Active)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPhase3Active)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPhase4Active)));
            RaisePhaseStateChanged();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.AutomationStatusLabel), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(MainWindowViewModel.AutomationUpdatedAt), StringComparison.Ordinal))
        {
            RaisePhaseStateChanged();
        }
    }

    private void RaiseSnapshotChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTaskRole)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTaskDependencies)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTaskRisk)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTaskExecutionSummary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReviewSummary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReviewSectionCount)));
    }

    private void RaisePhaseStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Phase1StateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Phase2StateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Phase3StateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Phase4StateText)));
    }

    private string BuildPhaseStateText(int phaseIndex, bool isActive)
    {
        if (isActive)
        {
            return $"进行中 · 最近更新 {Core.AutomationUpdatedAt}";
        }

        var currentPhaseIndex = ResolvePhaseIndex();
        var isCompleted = currentPhaseIndex > phaseIndex ||
            (currentPhaseIndex == phaseIndex &&
             string.Equals(Core.AutomationStatusLabel, AutomationStageStatus.Completed.ToString(), StringComparison.Ordinal));

        return isCompleted ? "已完成" : "未开始";
    }

    private int ResolvePhaseIndex()
    {
        return Core.AutomationPhaseLabel switch
        {
            "Phase 1 · 项目调研" => 1,
            "Phase 2 · 计划编排" => 2,
            "Phase 3 · 任务执行" => 3,
            "Phase 4 · 复核验收" => 4,
            _ => 0,
        };
    }
}
