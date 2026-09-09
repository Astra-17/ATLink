using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ATLink.Core;

namespace ATLink.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? allowed = null) : ICommand
{
    public bool CanExecute(object? parameter) => allowed?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public ObservableCollection<DatabaseTable> Tables { get; } = [];
    public DatabaseDocument? Document { get; private set; }
    public DatabaseDocument? Localization { get; private set; }
    public bool HasDocument => Document is not null;
    public int TableCount => Tables.Count;
    public int PlayersCount => Count("players");
    public int TeamsCount => Count("teams");
    public int LeaguesCount => Count("leagues");
    public int TransfersCount => Count("teamplayerlinks");
    public int StadiumsCount
    {
        get
        {
            int stadiums=Count("stadiums");
            return stadiums!=0?stadiums:Count("ssfstadiums");
        }
    }
    private int Count(string name) => Tables.FirstOrDefault(t => t.Name == name)?.RowCount ?? 0;

    private string screen = "Home";
    public string Screen { get => screen; set { screen = value; Notify(); } }
    private object? workspacePage;
    public object? Page { get => workspacePage; set { workspacePage = value; Notify(); } }
    public Action? ResetWorkspace { get; set; }
    public ICommand ShowLauncherCommand => new RelayCommand(() => { ResetWorkspace?.Invoke(); Screen = HasDocument ? "Launcher" : "Home"; }, () => !Busy);
    public ICommand ShowTablesCommand => new RelayCommand(() => { ResetWorkspace?.Invoke(); Screen = "Tables"; }, () => HasDocument && !Busy);
    public ICommand PreviousCommand => new RelayCommand(() => { page--; RefreshPage(); }, () => page > 0 && !Busy);
    public ICommand NextCommand => new RelayCommand(() => { page++; RefreshPage(); }, () => (page + 1) * PageSize < (Rows?.Count ?? 0) && !Busy);

    private string tableFilter = "";
    public string TableFilter { get => tableFilter; set { tableFilter = value; Notify(); Notify(nameof(FilteredTables)); } }
    public IEnumerable<DatabaseTable> FilteredTables => Tables.Where(t => t.Name.Contains(TableFilter, StringComparison.OrdinalIgnoreCase));

    private DatabaseTable? selected;
    public DatabaseTable? SelectedTable
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            searchText = "";
            page = 0;
            if (Rows is not null) Rows.RowFilter = "";
            searchColumn = selected is null ? null : TableOrdering.IdColumn(selected) ?? Columns.FirstOrDefault();
            foreach (var name in new[] { nameof(SelectedTable), nameof(Rows), nameof(TableTitle), nameof(Columns), nameof(SearchText), nameof(SearchColumn), nameof(FieldInfo) }) Notify(name);
            ApplyDefaultSort();
        }
    }
    public IEnumerable<string> Columns => SelectedTable?.Fields.Select(f => f.Name) ?? [];
    private string? searchColumn;
    public string? SearchColumn { get => searchColumn; set { searchColumn = value; Notify(); Notify(nameof(FieldInfo)); Filter(); } }
    private string searchText = "";
    public string SearchText { get => searchText; set { searchText = value; Notify(); Filter(); } }
    private bool exactMatch;
    public bool ExactMatch { get => exactMatch; set { exactMatch = value; Notify(); Filter(); } }
    public string FieldInfo
    {
        get
        {
            var descriptor = SelectedTable?.Fields.FirstOrDefault(f => f.Name == SearchColumn);
            return descriptor is null ? "" : $"{descriptor.Name} · {descriptor.Depth} bits";
        }
    }
    private void Filter()
    {
        if (Rows is null) return;
        if (string.IsNullOrEmpty(SearchText) || SearchColumn is null) Rows.RowFilter = "";
        else
        {
            string column = SearchColumn.Replace("\\", "\\\\").Replace("]", "\\]");
            string exact = SearchText.Replace("'", "''");
            string pattern = string.Concat(SearchText.Select(c => c switch { '\'' => "''", '[' => "[[]", ']' => "[]]", '%' => "[%]", '*' => "[*]", _ => c.ToString() }));
            Rows.RowFilter = ExactMatch ? $"[{column}] = '{exact}'" : $"[{column}] LIKE '%{pattern}%'";
        }
        page = 0;
        RefreshPage();
    }
    private int page;
    private int pageSize = 100;
    public int[] PageSizes { get; } = [50, 100, 250, 500];
    public int PageSize { get => pageSize; set { pageSize = value; page = 0; Notify(); RefreshPage(); } }
    public DataView? Rows => SelectedTable?.Data.DefaultView;
    private string[] numericSortColumns = [];
    private bool numericSortAscending;
    public string? ActiveSortColumn { get; private set; }
    public bool ActiveSortAscending { get; private set; } = true;
    public IReadOnlyList<DataRowView> GridRows
    {
        get
        {
            if (Rows is null) return [];
            IEnumerable<DataRowView> ordered = Rows.Cast<DataRowView>();
            if (numericSortColumns.Length > 0)
            {
                double Key(DataRowView row, string column) => double.TryParse(Convert.ToString(row[column], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NegativeInfinity;
                var result = numericSortAscending ? ordered.OrderBy(row => Key(row, numericSortColumns[0])) : ordered.OrderByDescending(row => Key(row, numericSortColumns[0]));
                for (int i = 1; i < numericSortColumns.Length; i++)
                {
                    int index = i;
                    result = numericSortAscending ? result.ThenBy(row => Key(row, numericSortColumns[index])) : result.ThenByDescending(row => Key(row, numericSortColumns[index]));
                }
                ordered = result;
            }
            return ordered.Skip(page * PageSize).Take(PageSize).ToArray();
        }
    }
    public string PageInfo => Rows is null || Rows.Count == 0 ? "0 rows" : $"{page * PageSize + 1:N0}–{Math.Min((page + 1) * PageSize, Rows.Count):N0} / {Rows.Count:N0}";
    public void RefreshPage() { page = Math.Clamp(page, 0, Math.Max(0, ((Rows?.Count ?? 0) - 1) / PageSize)); Notify(nameof(GridRows)); Notify(nameof(PageInfo)); CommandManager.InvalidateRequerySuggested(); }
    public void Sort(string column, bool ascending) => Sort([column], ascending);
    public void Sort(IReadOnlyList<string> columns, bool ascending)
    {
        if (Rows is null) return;
        var known = columns.Where(column => SelectedTable?.Fields.Any(field => field.Name == column) == true).ToArray();
        bool numeric = known.Length > 0 && known.All(column => SelectedTable!.Fields.First(f => f.Name == column).Type is 3 or 4);
        numericSortColumns = numeric ? known : [];
        numericSortAscending = ascending;
        ActiveSortColumn = known.FirstOrDefault();
        ActiveSortAscending = ascending;
        Rows.Sort = numeric || known.Length == 0 ? "" : string.Join(',', known.Select(column => $"[{column.Replace("]", "\\]")}] {(ascending ? "ASC" : "DESC")}"));
        page = 0;
        Notify(nameof(ActiveSortColumn));
        Notify(nameof(ActiveSortAscending));
        RefreshPage();
    }
    private void ApplyDefaultSort()
    {
        if (SelectedTable is null || Rows is null)
        {
            numericSortColumns = [];
            ActiveSortColumn = null;
            RefreshPage();
            return;
        }
        var columns = TableOrdering.IdColumns(SelectedTable);
        if (columns.Count == 0) Sort([], true);
        else Sort(columns, true);
    }
    public string TableTitle => SelectedTable?.Name ?? "No table selected";
    private string status = "Select an editor to open your database.";
    public string Status { get => status; set { status = value; Notify(); } }
    private string title = "EA SPORTS FC Database Editor";
    public string DocumentTitle { get => title; private set { title = value; Notify(); Notify(nameof(WindowTitle)); } }
    public string WindowTitle => HasDocument ? "ATLink · " + DocumentTitle : "ATLink · Database Studio";
    private bool busy;
    public bool Busy { get => busy; set { busy = value; Notify(); CommandManager.InvalidateRequerySuggested(); } }
    public void Load(DatabaseDocument doc, DatabaseDocument? localization = null)
    {
        ResetWorkspace?.Invoke();
        Document = doc; Localization = localization;
        Tables.Clear();
        foreach (var table in doc.Tables) Tables.Add(table);
        if(localization is not null)foreach(var table in localization.Tables)Tables.Add(table);
        TableFilter = "";
        SelectedTable = Tables.FirstOrDefault(t => t.Name == "players") ?? Tables.FirstOrDefault();
        DocumentTitle = string.IsNullOrWhiteSpace(doc.DisplayName) ? System.IO.Path.GetFileName(doc.SourcePath) : doc.DisplayName;
        foreach (var name in new[] { nameof(HasDocument), nameof(TableCount), nameof(PlayersCount), nameof(TeamsCount), nameof(LeaguesCount), nameof(TransfersCount), nameof(StadiumsCount), nameof(WindowTitle), nameof(Page) }) Notify(name);
        Status = $"{Tables.Count} tables loaded · {DocumentTitle}. Changes are saved to a new file.";
        Screen = "Launcher";
    }
    public void Close()
    {
        ResetWorkspace?.Invoke();
        Document = null; Localization = null; SelectedTable = null; Tables.Clear(); TableFilter = "";
        DocumentTitle = "EA SPORTS FC Database Editor";
        Notify(nameof(HasDocument)); Notify(nameof(WindowTitle)); Notify(nameof(Page)); Screen = "Home";
    }
    public void Revert()
    {
        if (Document is null) return;
        foreach (var table in Tables) table.Data.RejectChanges();
        RefreshPage(); Status = "Changes discarded.";
    }
}


