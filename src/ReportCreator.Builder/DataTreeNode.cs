using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReportCreator.Builder;

public sealed class DataTreeNode : INotifyPropertyChanged
{
    private string _title = "";
    private string _detail = "";
    private bool _isExpanded;
    private bool _isSelected;

    public string Key { get; set; } = "";

    public string? FieldPath { get; set; }

    public bool IsRoot { get; init; }

    public bool CanAdd { get; init; }

    public List<DataTreeNode> Children { get; } = [];

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
