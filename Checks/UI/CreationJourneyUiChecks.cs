using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.ViewModels;
using ATLink.Views;

internal static class CreationJourneyUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        string data=Path.Combine(root,"files/FC27"),meta=Path.Combine(data,"fifa_ng_db-meta.xml"),loc=Path.Combine(data,"loc/eng_us-meta.xml");
        using var c=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(data,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(data,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(data,"loc/eng_us.db"),loc),meta,loc);
        c.SetLanguages(new StudioData(Path.Combine(root,"files"),game:"FC27").Languages.Where(l=>l.Code is "eng_us" or "fre_fr" or "ger_de").ToArray());
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        void Invoke(FootballCreationWizard wizard,string method,object sender)=>typeof(FootballCreationWizard).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(wizard,[sender,new RoutedEventArgs()]);
        IEnumerable<T> Find<T>(DependencyObject node) where T:DependencyObject
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);if(child is T t)yield return t;foreach(var nested in Find<T>(child))yield return nested;}}
        foreach(var kind in Enum.GetValues<FootballCompetitionKind>())
        {
            var w=new FootballCreationWizard(c,84,_=>{});var m=w.Model;var tabs=(TabControl)w.FindName("Steps");
            Check(tabs.Items.Count==10,"Expected ten journey steps");
            LinkedUiRenderChecks.Render(w,"journey-format-"+kind);
            var choice=m.Types.Single(t=>t.Kind==kind);
            Invoke(w,"ChooseFormat",new Button{DataContext=choice});Check(tabs.SelectedIndex==(int)FootballCreationStep.Identity&&choice.Selected,"Format click did not advance/select");
            m.Name="Journey "+kind;var first=m.Format.Phases[0];string participants=first.Participants;
            tabs.SelectedIndex=0;Invoke(w,"ChooseFormat",new Button{DataContext=choice});Check(m.Name=="Journey "+kind&&m.Format.Phases[0].Participants==participants,"Reselecting format reset draft");
            m.Names.EnglishShort="Journey";m.Names.FrenchFull="Parcours";var german=m.Names.Languages.Single(l=>l.Code=="ger_de");german.FullName="Wettbewerb";
            var names=m.Names.Snapshot(c,m.Name);Check(names.Single(n=>n.Language=="ger_de").ShortName=="Wettbewerb"&&names.Single(n=>n.Language=="fre_fr").FullName=="Parcours","Multilingual names/fallback lost");
            Check(m.ValidateJourneyStep(FootballCreationStep.Identity),m.Error);
            tabs.SelectedIndex=(int)FootballCreationStep.Path;LinkedUiRenderChecks.Render(w,"journey-path-"+kind);
            Check(Find<Button>(w).Any(b=>b.Content?.ToString()=="+ Add a phase"&&b.Visibility==Visibility.Visible)==(kind==FootballCompetitionKind.Custom),"Add phase visibility");
            if(kind is FootballCompetitionKind.GroupsAndKnockout or FootballCompetitionKind.Custom)
            {
                first=m.Format.Phases[0];Check(first.ParticipantChoices.All(n=>int.Parse(n)%2==0),"Odd group participant choice");
                first.Participants="18";Check(first.GroupChoices.All(n=>18%int.Parse(n)==0&&(18/int.Parse(n))%2==0),"Odd teams per group choice");
                first.Participants="20";first.Groups="2";first.Qualifiers="2";Check(m.Format.Phases[1].Participants=="4","Knockout input not synchronized");
                m.Format.Phases[1].Rounds="2";var copy=first.EditCopy();copy.Participants="19";Check(copy.Participants=="20","Phase dialog accepted odd group total");
            }
            tabs.SelectedIndex=(int)FootballCreationStep.Links;Check(tabs.SelectedIndex==(int)FootballCreationStep.Links,m.Error);
            int count=int.Parse(m.Format.Phases[0].Participants);foreach(var team in c.Teams.Take(count))m.Participants.Add(new(team));
            tabs.SelectedIndex=(int)FootballCreationStep.Teams;LinkedUiRenderChecks.Render(w,"journey-teams-"+kind);
            if(m.HasMatchups){Invoke(w,"Next",w);Check(tabs.SelectedIndex==(int)FootballCreationStep.Teams&&((TabControl)w.FindName("TeamsAndRulesTabs")).SelectedItem==w.FindName("MatchupsTab"),"Next skipped matchups");}
            tabs.SelectedIndex=(int)FootballCreationStep.Schedule;Check(tabs.SelectedIndex==(int)FootballCreationStep.Schedule,m.Error);
            foreach(var step in new[]{FootballCreationStep.Rewards,FootballCreationStep.Rules,FootballCreationStep.Advancement,FootballCreationStep.Review})
            {
                tabs.SelectedIndex=(int)step;Check(tabs.SelectedIndex==(int)step,m.Error);LinkedUiRenderChecks.Render(w,"journey-"+step+"-"+kind);
                Check(((Button)w.FindName("CreateButton")).Visibility==(step==FootballCreationStep.Review?Visibility.Visible:Visibility.Collapsed),"Create available outside review");
                if(step==FootballCreationStep.Rewards)Check(!Find<FootballRulesPanel>(w).Any(),"Rewards displays rules");
            }
            Check(((TextBlock)w.FindName("StepLabel")).Text=="Step 10 of 10","Journey counter");
            var dates=m.Format.Phases[0].EssentialRounds[0];dates.SetDrawDate=true;dates.DrawDate=null;
            Check(!m.ValidateJourneyStep(FootballCreationStep.Schedule),"Missing draw date accepted at Schedule");dates.DrawDate=new DateTime(2026,7,1);
            Check(m.ValidateJourneyStep(FootballCreationStep.Schedule),m.Error);dates.SetDrawDate=false;
            var prize=m.Format.Phases[0].EssentialRounds[0];string oldPrize=prize.Prize;var oldPrizeMode=prize.PrizeMode;prize.PrizeMode=prize.PrizeModes.Single(p=>p.Mode==FootballSettingMode.Value);prize.Prize="invalid";
            tabs.SelectedIndex=(int)FootballCreationStep.Path;tabs.SelectedIndex=(int)FootballCreationStep.Rewards;
            Check(tabs.SelectedIndex==(int)FootballCreationStep.Rewards,"Invalid prize trapped navigation before Rewards");
            Check(!m.ValidateJourneyStep(FootballCreationStep.Rewards),"Invalid prize was accepted");Check(!m.ValidateStep(2),"Strict creation accepted invalid prize");prize.Prize=oldPrize;prize.PrizeMode=oldPrizeMode;
            tabs.SelectedIndex=(int)FootballCreationStep.Review;Check(tabs.SelectedIndex==(int)FootballCreationStep.Review,m.Error);
            if(kind==FootballCompetitionKind.League)
            {
                m.Create();Check(m.Completed,m.Error);
                var competition=c.Objects.Single(o=>o.Id==m.CreatedCompetitionId);string key=c.CompetitionTranslationKeys(competition)[0];
                Check(c.ReadTranslations("ger_de").Single(n=>n.Key==key).Text=="Wettbewerb","German name not applied");
                string output=Path.Combine(root,"Checks/output/creation-journey-"+Guid.NewGuid().ToString("N"));c.Export(output);
                using(var reopened=c.OpenProject(output))Check(reopened.ReadTranslations("ger_de").Single(n=>n.Key==key).Text=="Wettbewerb","German name lost on export/reopen");
                c.Undo();
            }
        }
        var namesVm=new FootballCompetitionNamesViewModel();namesVm.Languages.Single(l=>l.Code=="jpn_jp").FullName="Pending";
        var available=c.Languages;c.SetLanguages([]);bool rejected=false;try{_=namesVm.Snapshot(c,"Test");}catch(InvalidDataException){rejected=true;}finally{c.SetLanguages(available);}
        Check(rejected&&namesVm.Languages.Single(l=>l.Code=="jpn_jp").FullName=="Pending","Unavailable translation was lost or accepted");
        Console.WriteLine("PASS ten-step creation journey, format cards, group choices, translations and responsive renders");
    }
}
