using System.IO;
using ATLink.Core;
using ATLink.ViewModels;
using ATLink.Views;

internal static class CreationLinkedGraphUiChecks
{
    public static void Run(CompetitionProjectContext c,CompetitionProjectContext reference,CompetitionArtwork[]? artwork=null)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var before=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        int beforeObjects=c.Objects.Count,beforeInserts=c.Inserts.Count;
        string[] upperTeams=reference.Rows("initteams.txt").Where(r=>r[0]=="2011").OrderBy(r=>int.Parse(r[1])).Select(r=>r[2]).ToArray();
        string[] lowerTeams=reference.Rows("initteams.txt").Where(r=>r[0]=="2000").OrderBy(r=>int.Parse(r[1])).Select(r=>r[2]).ToArray();
        string[] extra=c.Teams.Select(t=>t.Id).Except(upperTeams.Concat(lowerTeams)).Take(2).ToArray();
        void Configure(FootballCreationViewModel vm,bool lower)
        {
            vm.Type=vm.Types.Single(t=>t.Kind==FootballCompetitionKind.GroupsAndKnockout);vm.Region=vm.Regions.Single(r=>r.Id==84);
            vm.Name=lower?"Draft Community":"Draft Super";
            vm.Format.SetPhases([
                new(){Name="Groups",Kind=FootballPhaseKind.Groups,Participants=20,Groups=2,Legs=1,QualifiersPerGroup=lower?2:8},
                new(){Name="Knockout",Kind=FootballPhaseKind.Knockout,Participants=lower?4:16,Rounds=lower?2:4,Legs=lower?2:1,FinalLegs=1}
            ]);
            foreach(string team in lower?lowerTeams.Concat(extra):upperTeams)vm.Participants.Add(new(c.Teams.Single(t=>t.Id==team)));
            vm.Matchups.Synchronize();vm.Matchups.SelectedModel="Crossed seeds";vm.Matchups.ApplyModel();
            vm.Calendar.Mode="Choose matchday dates";vm.Calendar.Synchronize();vm.Calendar.Matchdays.Clear();
            int[] groups=[234,279,283,322,325,455,458,512,521];
            foreach(var phase in vm.Calendar.Phases)
            {
                int[] dates=phase.Index==0?groups:lower?phase.Index==1?[529,532]:[535]:[new[]{526,529,532,535}[phase.Index-1]];
                for(int i=0;i<dates.Length;i++)vm.Calendar.Matchdays.Add(new(){PhaseIndex=phase.Index,PhaseKey=phase.Key,PhaseName=phase.Name,
                    Round=i+1,Date=c.ReferenceDate.AddDays(dates[i]).ToDateTime(TimeOnly.MinValue),Kickoff="21:00",
                    Minimum=phase.Matches.ToString(),Maximum=phase.Matches.ToString(),Capacity=phase.Matches});
            }
            vm.Links.RefreshChoices();
        }
        var rootWizard=new FootballCreationWizard(c,84,_=>{});
        var root=rootWizard.Model;Configure(root,false);
        if(artwork is not null){CompetitionArtworkUiChecks.Wait(root.Format.Artwork.ImportAsync(true,artwork[0].LogoPng));CompetitionArtworkUiChecks.Wait(root.Format.Artwork.ImportAsync(false,artwork[0].TitlePng));}
        Check(rootWizard.FindName("CreationLinksTab") is System.Windows.Controls.TabItem,"Missing actual creation tab.");
        var childWizard=new FootballCreationWizard(c,84,_=>throw new Exception("Child created a native competition before parent."),
            root,root.Links.AcceptNew);
        var child=childWizard.Model;Configure(child,true);
        child.Links.Higher=child.Links.NeighbourChoices.Single(n=>n.Reference?.DraftKey==root.DraftKey);
        child.Links.Promotion=child.Links.PromotionChoices.Single(p=>p.Id==1);
        child.Links.AddQualifiers=true;child.Links.QualifierName="Draft Community Playoffs";
        child.Links.QualifierDestination=child.Links.ReceivingChoices.Single(n=>n.Reference?.DraftKey==child.DraftKey);
        child.Links.ExcludedCompetition=child.Links.Choices.Single(n=>n.Reference?.DraftKey==root.DraftKey);
        child.Links.CalendarSource=child.Links.Choices.Single(n=>n.Reference?.DraftKey==root.DraftKey);
        child.Links.UseEuropeanQuotas();
        if(artwork is not null)
        {
            CompetitionArtworkUiChecks.Wait(child.Format.Artwork.ImportAsync(true,artwork[1].LogoPng));CompetitionArtworkUiChecks.Wait(child.Format.Artwork.ImportAsync(false,artwork[1].TitlePng));
            CompetitionArtworkUiChecks.Wait(child.Links.QualifierArtwork.ImportAsync(true,artwork[2].LogoPng));CompetitionArtworkUiChecks.Wait(child.Links.QualifierArtwork.ImportAsync(false,artwork[2].TitlePng));
        }
        foreach(var club in child.Links.DirectClubs)club.Selected=lowerTeams.Contains(club.Club.Id);
        foreach(var team in child.Participants.Where(t=>!lowerTeams.Contains(t.Id)).ToArray())child.Participants.Remove(team);
        Check(child.Participants.Count==18,"Direct-entry test needs exactly 18 selected clubs.");
        child.Create();Check(child.Completed,"Nested draft rejected: "+child.Error);
        Check(root.Links.NewDrafts.Count==1,"Nested draft was not attached to the parent.");
        Check(c.Objects.Count==beforeObjects&&c.Inserts.Count==beforeInserts&&c.Project.Files.All(f=>f.Text==before[f.RelativePath]),
            "Creating a child draft changed the native project.");

        root.Links.Review();root.UpdateReview();
        ((System.Windows.Controls.TabControl)rootWizard.FindName("Steps")).SelectedIndex=1;
        ((System.Windows.Controls.TabControl)rootWizard.FindName("TeamsAndRulesTabs")).SelectedItem=rootWizard.FindName("CreationLinksTab");
        LinkedUiRenderChecks.Render(rootWizard,"creation-linked");
        Check(root.Review.Contains("Draft Community")&&root.Review.Contains("Linked competitions"),"Linked drafts absent from Review.");
        Check(root.Links.Promotion?.Id==1,"Promotion rule did not synchronize between reciprocal draft choices.");
        var graph=root.GraphPlan();
        string undoBefore=c.UndoLabel;
        bool failed=false;
        try{FootballCompetitionGraph.Create(c,graph with{Playoffs=graph.Playoffs.Select(p=>p with{Name="Invalid,name"}).ToArray()});}
        catch(InvalidDataException){failed=true;}
        Check(failed&&c.Objects.Count==beforeObjects&&c.Inserts.Count==beforeInserts&&c.Project.Files.All(f=>f.Text==before[f.RelativePath]),
            "Failed linked creation left partial competitions.");
        Check(c.UndoLabel==undoBefore,"Failed nested creation left inner undo records.");
        if(artwork is not null)Check(c.Artwork.Count==0,"Failed linked creation leaked artwork.");
        root.Create();Check(root.Completed,"Linked creation failed: "+root.Error);
        int upper=root.CreatedCompetitionId;
        var link=FootballLinkedCompetitions.Divisions(c).Single(l=>l.Upper==upper);
        int lower=link.Lower;
        Check(link.Promotion==FootballPromotionSource.SemifinalWinners&&link.QualifyingPlaces==2,"Draft relationships were not compiled.");
        var qualifier=FootballLinkedCompetitions.QualifiersFor(c,lower).Single();
        if(artwork is not null)
        {
            Check(c.Artwork[upper].Fingerprint==artwork[0].Fingerprint&&c.Artwork[lower].Fingerprint==artwork[1].Fingerprint&&c.Artwork[qualifier.Competition].Fingerprint==artwork[2].Fingerprint,"Linked competitions lost their distinct artwork.");
            Check(c.PrepareFifaModFiles().Count(f=>f.GamePath.StartsWith("data/ui/imgAssets/"))==30,"Linked graph should export thirty artwork resources.");
        }
        Check(c.Rows("tasks.txt").Count(r=>r[0]==qualifier.Competition.ToString()&&r[2]=="FillFromLeagueMaxFromCountry")==21,"Missing draft quotas.");
        foreach(var (id,count) in new[]{(upper,20),(lower,18)})
            Check(c.Rows("tasks.txt").Where(r=>r[1]=="end"&&r[2]=="UpdateTable"&&r[3]==id.ToString()).Select(r=>int.Parse(r[6])).Order()
                .SequenceEqual(Enumerable.Range(1,count)),"Incomplete draft season table.");
        var lowerGroup=FootballLinkedCompetitions.GroupPhase(c,lower);
        var upperGroup=FootballLinkedCompetitions.GroupPhase(c,upper);
        Check(c.Rows("schedule.txt").Where(r=>r[0]==lowerGroup.NativeId.ToString()).Select(r=>(r[1],r[2],r[5]))
            .SequenceEqual(c.Rows("schedule.txt").Where(r=>r[0]==upperGroup.NativeId.ToString()).Select(r=>(r[1],r[2],r[5]))),"Draft calendar source ignored.");
        c.Undo();Check(c.Objects.Count==beforeObjects&&c.Inserts.Count==beforeInserts,"One Undo did not remove the whole graph.");
        if(artwork is not null)Check(c.Artwork.Count==0,"One Undo did not remove linked artwork.");
        c.Redo();Check(c.Objects.Any(o=>o.Id==upper)&&FootballLinkedCompetitions.QualifiersFor(c,lower).Count==1,"Graph redo failed.");
        if(artwork is not null)Check(c.Artwork.Count==3,"Redo did not restore linked artwork.");
        c.Undo();
        Check(c.Project.Files.All(f=>f.Text==before[f.RelativePath]),"Graph checks did not restore the project.");

        // Abandoning the root or removing an accepted child operates only on draft objects.
        root.Links.SelectedDraft=child;root.Links.RemoveNew();
        Check(root.Links.NewDrafts.Count==0&&root.Links.Lower?.Reference is null,"Removing a linked draft left a dangling reference.");
        Check(c.Objects.Count==beforeObjects,"Cancelled drafts left native competitions.");
        Console.WriteLine("PASS Add competition graph: real Teams tab, deferred child/playoff drafts, Review, initial clubs, dynamic quotas, shared dates, reciprocal policies, atomic rollback, one-step Undo/Redo and cancellation.");
    }
}

