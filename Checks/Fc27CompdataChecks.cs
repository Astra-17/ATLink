using ATLink.Core;
internal static class Fc27CompdataChecks
{
    public static void Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        string source=Path.Combine(root,"files/FC27/compdata");
        CompdataProject Open()=>CompdataProject.Open(source);
        var project=Open();project.Labels=new CompetitionLabels(DatabaseDocument.Open(Path.Combine(root,"files/FC27/eng_us.db"),Path.Combine(root,"files/FC27/eng_us-meta.xml")));
        Check(TournamentBuilder.Objects(project).Where(o=>o.Kind==3).All(o=>!o.DisplayName.Contains("name unavailable")&&!o.DisplayName.StartsWith("TrophyName")),"Competition translations");
        Check(project.Files.Count==13,"Missing bundled files");Check(CompetitionTools.Choices(project).Count==93,"Native competitions");
        Check(CompetitionEditing.Validate(project).Count==0,"Native validation: "+string.Join(";",CompetitionEditing.Validate(project).Distinct()));
        foreach(var file in project.Files)Check(new CompdataTable(file).Serialize()==file.Text,"Untouched text changed "+file.RelativePath);
        string target=Path.Combine(root,"Checks/output/compdata-"+Guid.NewGuid().ToString("N"));
        try{project.SaveAs(target);foreach(var file in project.Files)Check(File.ReadAllBytes(Path.Combine(source,file.RelativePath)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(target,file.RelativePath))),"Native export changed bytes");}finally{if(Directory.Exists(target))Directory.Delete(target,true);}
        var weather=project.Files.Single(f=>f.RelativePath=="weather.txt");var table=new CompdataTable(weather);Check(table.Data.Columns.Count==9&&table.Data.Columns[7].ColumnName=="sunset","Weather columns");table.Data.Rows[0][7]="1615";weather.Text=table.Serialize();Check(CompetitionEditing.Validate(project).Count==0,"Weather edit rejected");table.Data.Rows[0][2]="101";weather.Text=table.Serialize();Check(CompetitionEditing.Validate(project).Any(e=>e.StartsWith("weather.txt")),"Bad weather accepted");
        foreach(var format in new[]{TournamentFormat.League,TournamentFormat.Groups,TournamentFormat.Cup})
        {
            project=Open();var parent=TournamentBuilder.Objects(project).First(o=>o.Kind==2);
            int groups=format==TournamentFormat.Groups?2:1;
            var draft=new TournamentDraft(parent.Id,"C999999","ATLink test",format,groups,4,Enumerable.Range(1,groups*4).Select(n=>n.ToString()).ToArray(),true,new DateOnly(2026,7,1),new DateOnly(2026,8,1),7,"15:00");
            CompetitionTools.Apply(project,TournamentBuilder.Preview(project,draft));Check(CompetitionEditing.Validate(project).Count==0,"New "+format+": "+string.Join(";",CompetitionEditing.Validate(project).Distinct()));
            try{project.SaveAs(target);Check(CompetitionEditing.Validate(CompdataProject.Open(target)).Count==0,"Exported tournament validation");}finally{if(Directory.Exists(target))Directory.Delete(target,true);}
        }
        project=Open();CompetitionTools.Apply(project,CompetitionTools.PreviewClone(project,CompetitionTools.Choices(project)[0].Id,"C999998","Clone test"));Check(CompetitionEditing.Validate(project).Count==0,"Clone validation");
        project=Open();var advancement=project.Files.Single(f=>f.RelativePath=="advancement.txt");advancement.Text+="\n999999,1,999998,1\n";Check(CompetitionEditing.Validate(project).Count>0,"Missing progression object accepted");
        Console.WriteLine("PASS FC27 Compdata: 93 competitions, native validation, byte-identical export, nine-column weather edits, invalid inputs, league/groups/cup creation, clone and export/reopen");
    }
}
