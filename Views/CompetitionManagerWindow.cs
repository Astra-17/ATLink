using System.Data;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public sealed class CompetitionManagerWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public CompetitionManagerWindow(CompdataProject project)
    {
        var snapshots=project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text,StringComparer.OrdinalIgnoreCase);
        var draftProject=new CompdataProject{SourceFolder=project.SourceFolder,Files=project.Files.Select(f=>new CompdataFile{RelativePath=f.RelativePath,Text=f.Text,InitialText=f.Text,Encoding=f.Encoding,Original=f.Original}).ToArray()};
        var files=draftProject.Files.ToList();
        var schemas=new[]{("settings.txt","Règles"),("tasks.txt","Sources d'équipes"),("schedule.txt","Calendrier"),("standings.txt","Places"),("advancement.txt","Progression"),("initteams.txt","Équipes initiales"),("weather.txt","Météo")};
        foreach(var (path,_) in schemas)if(!files.Any(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase)))files.Add(new(){RelativePath=path,Text="",InitialText="",Original=[],Encoding=new UTF8Encoding(false)});
        draftProject.Files=files;
        var objects=TournamentBuilder.Objects(project);
        var dock=new DockPanel{Margin=new Thickness(16)};Content=dock;StudioWindow.Style(this,dock);
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);
        var back=new Button{Content="Back",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};header.Children.Add(back);back.Click+=(_,_)=>Closed?.Invoke(false);
        header.Children.Add(new TextBlock{Text="Choisir un objet pour éditer ses règles et ses descendants",FontSize=20,Margin=new Thickness(0,0,0,8)});
        var selection=new ComboBox{ItemsSource=objects,SelectedIndex=0,Margin=new Thickness(0,0,0,10)};header.Children.Add(selection);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};footer.Children.Add(status);
        var apply=new Button{Content="Appliquer les modifications en mémoire",HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(20,8,20,8)};footer.Children.Add(apply);
        var tabs=new TabControl();dock.Children.Add(tabs);
        var tables=new Dictionary<string,CompdataTable>(StringComparer.OrdinalIgnoreCase);var grids=new List<DataGrid>();
        HashSet<string> Scope()
        {
            if(selection.SelectedItem is not CompetitionObject obj)return [];
            var ids=new HashSet<string>{obj.Id.ToString()};bool changed;
            do{changed=false;foreach(var child in objects)if(ids.Contains(child.ParentId.ToString())&&ids.Add(child.Id.ToString()))changed=true;}while(changed);
            return ids;
        }
        void Commit(){foreach(var grid in grids){if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))throw new InvalidOperationException("Terminer l'édition de la cellule.");}foreach(var (path,table) in tables)files.Single(f=>f.RelativePath==path).Text=table.Serialize();}
        void Refresh(){var ids=Scope();string filter=ids.Count==0?"1=0":"{0} IN ("+string.Join(',',ids.Select(id=>"'"+id+"'"))+")";foreach(var table in tables.Values)table.Data.DefaultView.RowFilter=string.Format(filter,"["+table.Data.Columns[0].ColumnName+"]");}
        foreach(var (path,label) in schemas)
        {
            var file=files.Single(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));var table=new CompdataTable(file);tables[file.RelativePath]=table;
            var panel=new DockPanel();
            var help=new TextBlock{Margin=new Thickness(8),TextWrapping=TextWrapping.Wrap,Text=path switch{"settings.txt"=>"Les règles de l'objet sélectionné remplacent celles de ses parents. Plusieurs valeurs par clé sont possibles.","tasks.txt"=>"Sources d'équipes : timing start/end, action et cible. Les paramètres dépendent de l'action.","advancement.txt"=>"Les positions commencent à 1. Une place de destination ne peut recevoir qu'une progression.","initteams.txt"=>"Les positions commencent à 0. Les identifiants doivent correspondre aux équipes de votre DB.","schedule.txt"=>"Jour = offset depuis la date de référence du jeu. Heure HHmm ; minimum de matchs ≤ maximum.","weather.txt"=>"Choisir un pays. Sec + pluie + neige = 100 ; couverture nuageuse indépendante.",_=>"Une ligne représente une place de classement, numérotée à partir de 1."}};
            DockPanel.SetDock(help,Dock.Top);panel.Children.Add(help);
            if(path=="weather.txt")
            {
                var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8)};DockPanel.SetDock(bar,Dock.Top);panel.Children.Add(bar);
                var preset=new ComboBox{ItemsSource=new[]{"temperate","cold","tropical","dry","rainy"},SelectedIndex=0,Width=150};bar.Children.Add(preset);
                var missing=new CheckBox{Content="Mois manquants seulement",IsChecked=true,Margin=new Thickness(10)};bar.Children.Add(missing);
                var generate=new Button{Content="Appliquer le climat"};bar.Children.Add(generate);
                generate.Click+=(_,_)=>{try{if(selection.SelectedItem is not CompetitionObject country||country.Kind!=2)throw new InvalidOperationException("Choisir un objet pays.");CompetitionEditing.WeatherPreset(table,country.Id,(string)preset.SelectedItem,missing.IsChecked==true);}catch(Exception ex){status.Text=ex.Message;}};
            }
            var grid=new DataGrid{ItemsSource=IdSortedView.Bind(table.Data.DefaultView),AutoGenerateColumns=true,CanUserAddRows=true,CanUserDeleteRows=true,Margin=new Thickness(8),EnableRowVirtualization=true};grids.Add(grid);panel.Children.Add(grid);
            grid.InitializingNewItem+=(_,e)=>{if(e.NewItem is DataRowView row&&selection.SelectedItem is CompetitionObject obj)row[0]=obj.Id.ToString();};
            tabs.Items.Add(new TabItem{Header=label,Content=panel});
        }
        var inherited=new TextBox{IsReadOnly=true,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(12),FontFamily=new System.Windows.Media.FontFamily("Consolas")};tabs.Items.Add(new TabItem{Header="Règles effectives",Content=inherited});
        var qualifications=new StackPanel{Margin=new Thickness(16)};
        qualifications.Children.Add(new TextBlock{Text="Choisir un groupe de classement, puis la position et son étiquette de qualification. Les sources d'équipes de la compétition cible se configurent dans l'onglet correspondant.",TextWrapping=TextWrapping.Wrap});
        var rank=new TextBox{Text="1",Margin=new Thickness(0,10,0,10)};qualifications.Children.Add(rank);
        string[] keys=["info_label_slot_champ","info_label_slot_ucl","info_label_slot_uel","info_label_slot_uecl","info_label_slot_ucl_qual","info_label_slot_uel_qual","info_label_slot_uecl_qual","info_label_slot_libert","info_label_slot_libert_qual","info_label_slot_sudame"];
        var kind=new ComboBox{ItemsSource=keys,SelectedIndex=0};qualifications.Children.Add(kind);
        var assign=new Button{Content="Définir la qualification pour cette place",Margin=new Thickness(0,12,0,0)};qualifications.Children.Add(assign);
        assign.Click+=(_,_)=>
        {
            try
            {
                if(selection.SelectedItem is not CompetitionObject group||group.Kind!=5||!int.TryParse(rank.Text,out int position)||position<1)throw new InvalidOperationException("Choisir un groupe et une position positive.");
                var slots=tables["standings.txt"].Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted);
                if(!slots.Any(r=>(string)r[0]==group.Id.ToString()&&(string)r[1]==position.ToString()))throw new InvalidOperationException("Cette place n'existe pas dans standings.");
                var settings=tables["settings.txt"];
                foreach(var row in settings.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted&&(string)r[0]==group.Id.ToString()&&(keys.Contains((string)r[1])||(string)r[1]=="info_slot_champ")&&(string)r[2]==position.ToString()).ToArray())row.Delete();
                settings.Data.Rows.Add(group.Id.ToString(),(string)kind.SelectedItem,position.ToString());
                if((string)kind.SelectedItem=="info_label_slot_champ")settings.Data.Rows.Add(group.Id.ToString(),"info_slot_champ",position.ToString());
                status.Text="Qualification ajoutée au brouillon.";
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        tabs.Items.Add(new TabItem{Header="Qualifications",Content=qualifications});
        var sources=new StackPanel{Margin=new Thickness(16)};
        sources.Children.Add(new TextBlock{Text="Choisir la compétition dans la liste principale, puis une cible et une source d'équipes. Les règles ajoutées s'exécutent dans l'ordre de tasks.txt.",TextWrapping=TextWrapping.Wrap});
        var target=new ComboBox{ItemsSource=objects.Where(o=>o.Kind is 4 or 5),Margin=new Thickness(0,10,0,10)};sources.Children.Add(target);
        var action=new ComboBox{ItemsSource=TeamSourceRules.Actions,Margin=new Thickness(0,0,0,10)};sources.Children.Add(action);
        var parameters=new StackPanel();sources.Children.Add(parameters);var parameterInputs=new List<TextBox>();
        action.SelectionChanged+=(_,_)=>{parameters.Children.Clear();parameterInputs.Clear();if(action.SelectedItem is TeamSourceAction definition)foreach(var label in definition.Parameters){parameters.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,6,0,4)});var input=new TextBox{Text="1"};parameters.Children.Add(input);parameterInputs.Add(input);}};action.SelectedIndex=0;
        var addSource=new Button{Content="Ajouter cette règle au brouillon",Margin=new Thickness(0,12,0,0)};sources.Children.Add(addSource);
        addSource.Click+=(_,_)=>{try{if(selection.SelectedItem is not CompetitionObject competition||target.SelectedItem is not CompetitionObject destination||action.SelectedItem is not TeamSourceAction definition)throw new InvalidOperationException("Compétition, cible et action requises.");var values=TeamSourceRules.Create(draftProject,competition.Id,destination.Id,definition.Code,parameterInputs.Select(i=>i.Text.Trim()).ToArray());tables["tasks.txt"].Data.Rows.Add(values.Cast<object>().ToArray());status.Text="Règle ajoutée ; relire l'ordre dans Sources d'équipes.";}catch(Exception ex){status.Text=ex.Message;}};
        tabs.Items.Add(new TabItem{Header="Ajouter une source",Content=new ScrollViewer{Content=sources,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var allocation=new StackPanel{Margin=new Thickness(16)};
        allocation.Children.Add(new TextBlock{Text="Choisir un pays pour ses allocations ou une confédération pour ses équipes spéciales. Les valeurs conservent leur ordre ; les pools spéciaux sont des triplets (valeur, valeur, ID de nation).",TextWrapping=TextWrapping.Wrap});
        var region=new ComboBox{ItemsSource=new[]{"UEFA","CONMEBOL"},SelectedIndex=0,Margin=new Thickness(0,10,0,10)};allocation.Children.Add(region);
        var valuesText=new TextBox{AcceptsReturn=true,Height=200,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};allocation.Children.Add(valuesText);
        string AllocationKey(bool special)=>((string)region.SelectedItem).ToLowerInvariant()+"_seeded_slots"+(special?"_special_teams":"");
        var readAllocation=new Button{Content="Lire les valeurs actuelles",Margin=new Thickness(0,10,0,10)};allocation.Children.Add(readAllocation);
        readAllocation.Click+=(_,_)=>{try{if(selection.SelectedItem is not CompetitionObject o||o.Kind is not (1 or 2))throw new InvalidOperationException("Choisir un pays ou une confédération.");string key=AllocationKey(o.Kind==1);valuesText.Text=string.Join("\n",tables["settings.txt"].Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted&&(string)r[0]==o.Id.ToString()&&(string)r[1]==key).Select(r=>(string)r[2]));}catch(Exception ex){status.Text=ex.Message;}};
        var writeAllocation=new Button{Content="Remplacer les allocations dans le brouillon"};allocation.Children.Add(writeAllocation);
        writeAllocation.Click+=(_,_)=>
        {
            try
            {
                if(selection.SelectedItem is not CompetitionObject o||o.Kind is not (1 or 2))throw new InvalidOperationException("Choisir un pays ou une confédération.");
                var values=valuesText.Text.Split([' ','\t','\r','\n',',',';'],StringSplitOptions.RemoveEmptyEntries);
                if(values.Any(v=>!int.TryParse(v,out int number)||number<0)||o.Kind==1&&values.Length%3!=0)throw new InvalidOperationException("Entiers non négatifs requis ; les pools spéciaux exigent des triplets.");
                string key=AllocationKey(o.Kind==1);var data=tables["settings.txt"].Data;
                foreach(var r in data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted&&(string)r[0]==o.Id.ToString()&&(string)r[1]==key).ToArray())r.Delete();
                foreach(string value in values)data.Rows.Add(o.Id.ToString(),key,value);
                status.Text="Allocations remplacées dans le brouillon.";
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        tabs.Items.Add(new TabItem{Header="Allocations continentales",Content=new ScrollViewer{Content=allocation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        void Effective(){Commit();if(selection.SelectedItem is CompetitionObject obj)inherited.Text=string.Join("\n",CompetitionEditing.InheritedSettings(draftProject,obj.Id).Select(s=>$"Objet {s.Source} · {s.Key} = {s.Value}"));}
        selection.SelectionChanged+=(_,_)=>{try{Commit();Refresh();Effective();}catch(Exception ex){status.Text=ex.Message;}};
        tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs)try{Effective();}catch(Exception ex){status.Text=ex.Message;}};
        apply.Click+=(_,_)=>
        {
            try
            {
                Commit();var baseline=CompetitionEditing.Validate(project).ToHashSet();var errors=CompetitionEditing.Validate(draftProject).Where(e=>!baseline.Contains(e)).ToArray();if(errors.Length>0)throw new InvalidOperationException(string.Join("\n",errors.Take(15)));
                var changes=files.Where(f=>f.Text!=snapshots.GetValueOrDefault(f.RelativePath,"")).Select(f=>new CompdataChange(f.RelativePath,snapshots.GetValueOrDefault(f.RelativePath),f.Text)).ToArray();
                CompetitionTools.Apply(project,new(changes,0));Closed?.Invoke(true);
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        Refresh();Effective();
    }
}
