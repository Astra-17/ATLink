using ATLink.Core;
using System.Text;
internal static class ExtendedChecks
{
    public static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        CompdataFile File(string path,string text)=>new(){RelativePath=path,Text=text,InitialText=text,Original=Encoding.UTF8.GetBytes(text),Encoding=new UTF8Encoding(false)};
        var project=new CompdataProject{SourceFolder=".",Files=new[]{File("compobj.txt","0,0,W,World,-1\n1,2,E,England,0\n"),File("settings.txt","0,rule,base\n1,rule,local\n1,list,a\n1,list,b\n")}};
        Check(CompetitionEditing.InheritedSettings(project,1).Count==3,"Inheritance/multi-value override");
        foreach(var climate in new[]{"temperate","cold","tropical","dry","rainy"})
        {
            var file=File("weather.txt","");var table=new CompdataTable(file);CompetitionEditing.WeatherPreset(table,1,climate,false);file.Text=table.Serialize();
            var copy=new CompdataProject{SourceFolder=".",Files=project.Files.Append(file).ToArray()};Check(CompetitionEditing.Validate(copy).Count==0,"Climate "+climate);
            Check(table.Data.Rows.Count==12,"Twelve weather months");CompetitionEditing.WeatherPreset(table,1,"temperate",true);Check(file.Text==table.Serialize(),"Preserve existing weather");
        }
        var generated=new CompdataProject{SourceFolder=".",Files=project.Files};
        var draft=new TournamentDraft(1,"C500","Cup",TournamentFormat.Cup,1,16,[],false,new DateOnly(2011,12,25),new DateOnly(2012,8,4),7,"15:00");
        CompetitionTools.Apply(generated,TournamentBuilder.Preview(generated,draft));Check(CompetitionEditing.Validate(generated).Count==0,"Generated cup rules/references");
        var cupObjects=TournamentBuilder.Objects(generated);var competition=cupObjects.Single(o=>o.Kind==3);var group=cupObjects.First(o=>o.Kind==5);
        var sourceRule=TeamSourceRules.Create(generated,competition.Id,group.Id,"FillWithTeam",["1","42"]);
        Check(sourceRule.SequenceEqual(new[]{competition.Id.ToString(),"start","FillWithTeam",group.Id.ToString(),"1","42","0"}),"Team-source parameter order");
        try{TeamSourceRules.Create(generated,competition.Id,group.Id,"FillFromCompTable",["999999","2"]);throw new Exception("Missing source accepted");}catch(InvalidDataException){}
        generated.Files=generated.Files.Append(File("tasks.txt",$"{competition.Id},start,FillFromCompTable,{group.Id},{competition.Id},2,0\n")).ToArray();
        CompetitionTools.Apply(generated,CompetitionTools.PreviewClone(generated,competition.Id.ToString(),"C501","Cloned cup"));
        var clonedRule=generated.Files.Single(f=>f.RelativePath=="tasks.txt").Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Last().Split(',');
        Check(clonedRule[0]==clonedRule[4]&&clonedRule[0]!=competition.Id.ToString(),"Clone task source references");
        foreach(int n in new[]{3,4,5,16,32})
        {
            var fixtures=TournamentBuilder.RoundRobin(Enumerable.Range(1,n).Select(i=>i.ToString()).ToArray(),true);
            Check(fixtures.Count==n*(n-1),"Fixture count");Check(fixtures.Select(f=>(f.Home,f.Away)).Distinct().Count()==fixtures.Count,"Duplicate pairing");
            Check(fixtures.GroupBy(f=>f.Round).All(g=>g.SelectMany(f=>new[]{f.Home,f.Away}).Distinct().Count()==g.Count()*2),"Double booking");
        }
        Console.WriteLine("PASS competition inheritance, five climates, round robins and generated cup validation");
        foreach(string position in PlayerRatings.Positions)
        foreach(int target in new[]{1,45,70,96,99})
        {
            var attributes=PlayerRatings.Generate(position,target,123);
            Check(PlayerRatings.RawOverall(position,attributes)==target,"Attribute target "+position+" "+target);
            Check(attributes.Values.All(v=>v is >=1 and <=99),"Attribute bounds");
        }
        Check(PlayerRatings.Potential(78,28,100000000,"ST")==78,"Veteran potential");
        Check(PlayerRatings.Potential(78,27,5000000,"CB")==78,"Late growth threshold");
        Check(PlayerRatings.Potential(78,27,80000000,"CB")>78,"Late premium growth");
        Check(PlayerRatings.Potential(70,20,25000000,"RW")>=78,"Youth growth");
        Check(PlayerRatings.Potential(76,24,6000000,"CDM")>=78,"Developing midfielder");
        Check(PlayerRatings.Potential(78,26,70000000,"CB")>PlayerRatings.Potential(78,26,70000000,"LW"),"Position growth");
        var input=new RatingInput(20,"ST",50000000,4000000,5000000,Seed:42);var result=PlayerRatings.Calculate(input);
        Check(result.Potential>=result.Overall&&result.Attributes.SequenceEqual(PlayerRatings.Calculate(input).Attributes),"Deterministic generation");
        Check(PlayerRatings.Calculate(input with{Age=29}).Potential==PlayerRatings.Calculate(input with{Age=29}).Overall,"Veteran integrated potential");
        var html="<h1><span class='shirt-number'>#9</span>Jean Test</h1><span itemprop='birthDate'>2001-05-14</span><span class='info-table__content--regular'>Position:</span><span>Avant-centre</span><span itemprop='height'>1,85 m</span><div class='data-header__market-value-wrapper'>12,50 mio. €</div>";
        var profile=PlayerProfileImport.Parse(html);Check(profile.Name=="Jean Test"&&profile.Height==185&&profile.Position=="ST"&&profile.BirthDate==new DateOnly(2001,5,14)&&profile.MarketValue==12500000,"Profile HTML parse");
        Check(PlayerProfileImport.ParseMoney("€500k")==500000,"Thousand market value");
        Check(PlayerProfileImport.Parse("{\"Name\":\"Test\",\"BirthDate\":\"2000-01-02\",\"MarketValue\":1000000}").BirthDate==new DateOnly(2000,1,2),"JSON profile");
        Check(PlayerProfileImport.ParseMoney("€1,000,000")==1000000,"Thousands separators");
        Check(PlayerProfileImport.ParseMoney("Last update Sep 9 2026")==null,"No fabricated price from date");
        Check(LanguageHash.Compute("")==0x80000000u&&LanguageHash.Compute("TeamName_1")==LanguageHash.Compute("teamname_1"),"Language hash boundary/case");
        string contextHtml="<h1>Jean Test</h1><span class='data-header__club'><a href='/x/startseite/verein/11' title='Club A'>Club A</a></span><span itemprop='affiliation'><a href='/x/wettbewerb/GB1'>Premier League</a></span><div class='box'><h2>Premier League</h2><table class='auflistung'><tr><td>2024</td></tr><tr><td>2023</td></tr></table></div>";
        var withClub=PlayerProfileImport.Parse(contextHtml);
        Check(withClub.Club=="Club A"&&withClub.League=="Premier League"&&withClub.WeightedTrophies==4,"Club/league/trophy context");
        Check(PlayerProfileImport.TrophyWeight("UEFA Champions League")==5,"Trophy weight");
        var clubPage=PlayerProfileImport.Merge(withClub,"<a class='data-header__market-value-wrapper'>€100.00m</a><li>Squad size: 25</li>");
        Check(clubPage.ClubMean==4000000,"Club mean from squad value");
        Check(FieldChoices.For("preferredfoot")!.Any(c=>c.Value=="1")&&FieldChoices.For("preferredposition1")!.Count==28,"Specialized selectors");
        var locRows=LocalizationFormat.ParseStrings("stringid\tsourcetext\nTeamName_1\tArsenal");
        Check(locRows[0][1]=="TeamName_1"&&locRows[0][2]=="Arsenal","Tab localization export");
        Console.WriteLine("PASS all position rating targets/bounds, potential reference scenarios, deterministic ratings and HTML/JSON profiles");
    }
}
